using Autodesk.Forge;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;

namespace Forge_Upload_DirectToS3
{
  public static class BinarytransferClient
  {
    public static string BASE_URL { get; set; }

    public static dynamic CREDENTIAL { get; set; }

    public static int UPLOAD_CHUNK_SIZE { get; set; }

    public static string CLIENT_ID { get; set; }
    public static string CLIENT_SECRET { get; set; }

    public static int MAX_RETRY { get; set; }


    /// <summary>
    /// Return the URLs to upload the file
    /// </summary>
    /// <param name="bucketKey">Bucket key</param>
    /// <param name="objectKey">Object key</param>
    /// <param name="parts">[parts=1] How many URLs to generate in case of multi-part upload</param>
    /// <param name="firstPart">B[firstPart=1] Index of the part the first returned URL should point to</param>
    /// <param name="uploadKey">[uploadKey] Optional upload key if this is a continuation of a previously initiated upload</param>
    /// <param name="minutesExpiration">[minutesExpiration] Custom expiration for the upload URLs (within the 1 to 60 minutes range). If not specified, default is 2 minutes.
    public static async Task<dynamic> getUploadUrls(string bucketKey, string objectKey, int? minutesExpiration, int parts = 1, int firstPart = 1, string uploadKey = null)
    {
      string endpoint = $"/buckets/{bucketKey}/objects/{HttpUtility.UrlEncode(objectKey)}/signeds3upload";

      RestClient client = new RestClient(BASE_URL);
      RestRequest request = new RestRequest(endpoint, RestSharp.Method.GET);
      request.AddHeader("Authorization", "Bearer " + CREDENTIAL.access_token);
      request.AddHeader("Content-Type", "application/json");
      request.AddParameter("parts", parts, ParameterType.QueryString);
      request.AddParameter("firstPart", firstPart, ParameterType.QueryString);

      if (!string.IsNullOrEmpty(uploadKey))
      {
        request.AddParameter("uploadKey", uploadKey, ParameterType.QueryString);
      }

      if (minutesExpiration != null)
      {
        request.AddParameter("minutesExpiration", minutesExpiration, ParameterType.QueryString);
      }

      var response = await client.ExecuteAsync(request);

      //Here we handle 429 for Get Upload URLs
      if (response.StatusCode == HttpStatusCode.TooManyRequests)
      {
        int retryAfter = 0;
        int.TryParse(response.Headers.ToList()
            .Find(x => x.Name == "Retry-After")
            .Value.ToString(), out retryAfter);
        Task.WaitAll(Task.Delay(retryAfter));
        return await getUploadUrls(bucketKey, objectKey, minutesExpiration, parts, firstPart, uploadKey);
      }

      return JsonConvert.DeserializeObject(response.Content);
    }

    /// <summary>
    /// Upload the FileStream to specified bucket
    /// </summary>
    /// <param name="bucketKey">Bucket key</param>
    /// <param name="objectKey">Object key</param>
    /// <param name="fileStream">FileStream from input file</param>
    public static async Task<dynamic> UploadToBucket(string bucketKey, string objectKey, FileStream fileStream)
    {
      long fileSize = fileStream.Length;
      int maxBatches = 25;
      int numberOfChunks = (int)Math.Round((double)(fileSize / UPLOAD_CHUNK_SIZE)) + 1;
      int partsUploaded = 0;
      long start = 0;
      List<string> uploadUrls = new List<string>();
      string uploadKey = null;

      using (BinaryReader reader = new BinaryReader(fileStream))
      {
        while (partsUploaded < numberOfChunks)
        {
          int attempts = 0;

          long end = Math.Min((partsUploaded + 1) * UPLOAD_CHUNK_SIZE, fileSize);

          long numberOfBytes = end - start;
          byte[] fileBytes = new byte[numberOfBytes];
          reader.BaseStream.Seek((int)start, SeekOrigin.Begin);
          int count = reader.Read(fileBytes, 0, (int)numberOfBytes);

          while (true)
          {
            attempts++;
            Console.WriteLine($"Uploading part {partsUploaded + 1}, attempt {attempts}");
            if (uploadUrls.Count == 0)
            {
              CREDENTIAL = await Get2LeggedTokenAsync(new Scope[] { Scope.DataRead, Scope.DataWrite, Scope.DataCreate });
              dynamic uploadParams = await getUploadUrls(bucketKey, objectKey, null, Math.Min(numberOfChunks - partsUploaded, maxBatches), partsUploaded + 1, uploadKey);
              uploadKey = uploadParams.uploadKey;
              uploadUrls = uploadParams.urls.ToObject<List<string>>();
            }

            string currentUrl = uploadUrls[0];
            uploadUrls.RemoveAt(0);

            try
            {
              var responseBuffer = await UploadBufferRestSharp(currentUrl, fileBytes);

              int statusCode = (int)responseBuffer.StatusCode;

              switch (statusCode)
              {
                case 403:
                  Console.WriteLine("403, refreshing urls");
                  uploadUrls = new List<string>();
                  break;
                case int n when (n >= 400):
                  throw new Exception(responseBuffer.Content);
                default:
                  goto NextChunk;
              }

            }
            catch (Exception ex)
            {
              Console.WriteLine(ex.Message);
              if (attempts == MAX_RETRY)
                throw;
            }
          }
        NextChunk:
          partsUploaded++;
          start = end;
          System.Console.WriteLine($"{partsUploaded.ToString()} parts uploaded!");

        }
      }

      var responseUpload = await CompleteUpload(bucketKey, objectKey, uploadKey);

      return responseUpload;
    }

    /// <summary>
    /// Upload the specific part through url
    /// </summary>
    /// <param name="url">URL to upload the specified part</param>
    /// <param name="buffer">Buffer array to upload</param>
    public static async Task<dynamic> UploadBufferRestSharp(string url, byte[] buffer)
    {
      RestClient client = new RestClient();
      RestRequest request = new RestRequest(url, RestSharp.Method.PUT);
      request.AddParameter("", buffer, ParameterType.RequestBody);

      var response = await client.ExecuteAsync(request);

      return response;
    }

    /// <summary>
    /// Finalizes the upload of a file to OSS.
    /// </summary>
    /// <param name="bucketKey">Bucket key</param>
    /// <param name="objectKey">Object key</param>
    /// <param name="uploadKey">[uploadKey] Optional upload key if this is a continuation of a previously initiated upload</param>
    public static async Task<dynamic> CompleteUpload(string bucketKey, string objectKey, string uploadKey)
    {
      string endpoint = $"/buckets/{bucketKey}/objects/{HttpUtility.UrlEncode(objectKey)}/signeds3upload";
      RestClient client = new RestClient(BASE_URL);
      RestRequest request = new RestRequest(endpoint, Method.POST);

      request.AddHeader("Authorization", "Bearer " + CREDENTIAL.access_token);
      request.AddHeader("Content-Type", "application/json");

      request.AddJsonBody(new { uploadKey = $"{uploadKey}" });

      var response = await client.ExecuteAsync(request);

      return response;
    }

    /// <summary>
    /// Return the URLs to upload the file
    /// </summary>
    /// <param name="bucketKey">Bucket key</param>
    /// <param name="objectKey">Object key</param>
    /// <param name="minutesExpiration">[minutesExpiration] Custom expiration for the upload URLs (within the 1 to 60 minutes range). If not specified, default is 2 minutes.
    public static async Task<dynamic> getDownloadUrl(string bucketKey, string objectKey, int? minutesExpiration)
    {
      string endpoint = $"/buckets/{bucketKey}/objects/{HttpUtility.UrlEncode(objectKey)}/signeds3download";
      RestClient client = new RestClient(BASE_URL);
      RestRequest request = new RestRequest(endpoint, RestSharp.Method.GET);
      request.AddHeader("Authorization", "Bearer " + CREDENTIAL.access_token);
      request.AddHeader("Content-Type", "application/json");

      if (minutesExpiration != null)
      {
        request.AddParameter("minutesExpiration", minutesExpiration, ParameterType.QueryString);
      }

      var response = await client.ExecuteAsync(request);

      //Here we handle 429 for Get Download URLs
      if (response.StatusCode == HttpStatusCode.TooManyRequests)
      {
        int retryAfter = 0;
        int.TryParse(response.Headers.ToList()
            .Find(x => x.Name == "Retry-After")
            .Value.ToString(), out retryAfter);
        Task.WaitAll(Task.Delay(retryAfter));
        return await getDownloadUrl(bucketKey, objectKey, minutesExpiration);
      }

      return JsonConvert.DeserializeObject(response.Content);
    }

    /// <summary>
    /// Download the specific part through url
    /// </summary>
    /// <param name="url">URL to download the file</param>
    public static byte[] DownloadBufferRestSharp(string url)
    {
      RestClient client = new RestClient();
      RestRequest request = new RestRequest(url, RestSharp.Method.GET);

      byte[] data = client.DownloadData(request);

      return data;
    }

    /// <summary>
    /// Return the byte array of the downloaded content
    /// </summary>
    /// <param name="bucketKey">Bucket key</param>
    /// <param name="objectKey">Object key</param>
    /// <param name="minutesExpiration">[minutesExpiration] Custom expiration for the upload URLs (within the 1 to 60 minutes range). If not specified, default is 2 minutes.
    public static async Task<byte[]> DownloadFromBucket(string bucketKey, string objectKey, int? minutesExpiration)
    {
      dynamic downloadParams = await getDownloadUrl(bucketKey, objectKey, minutesExpiration);

      if (downloadParams.status != "complete")
      {
        throw new Exception("File not available for download yet.");
      }

      byte[] downloadedBuffer = DownloadBufferRestSharp(downloadParams.url.ToString());

      return downloadedBuffer;

    }

    /// <summary>
    /// Get the access token from Autodesk
    /// </summary>
    public static async Task<dynamic> Get2LeggedTokenAsync(Scope[] scopes)
    {
      TwoLeggedApi oauth = new TwoLeggedApi();
      string grantType = "client_credentials";
      dynamic bearer = await oauth.AuthenticateAsync(
        CLIENT_ID,
        CLIENT_SECRET,
        grantType,
        scopes);
      return bearer;
    }
  }
}