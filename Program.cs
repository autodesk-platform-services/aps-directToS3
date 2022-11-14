using Forge_Upload_DirectToS3.test;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace Forge_Upload_DirectToS3
{
  class Program
  {
    static async Task Main(string[] args)
    {
      try
      {
        var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json", true, true).Build();

        BinarytransferClient.BASE_URL = "https://developer.api.autodesk.com/oss/v2";
        BinarytransferClient.MAX_RETRY = 5;
        BinarytransferClient.UPLOAD_CHUNK_SIZE = 5 * 1024 * 1024;
        BinarytransferClient.CLIENT_ID = configuration.GetSection("Forge_Client_Id").Value;
        BinarytransferClient.CLIENT_SECRET = configuration.GetSection("Forge_Client_Secret").Value;

        string bucketKey = configuration.GetSection("BucketKey").Value;
        string projectId = configuration.GetSection("ProjectId").Value;
        string folderId = configuration.GetSection("Folder_Urn").Value;

        string filePath = args[0];
        string objectKey = args[2];

        string option = $"{args[1].ToLower()}-{args[3].ToLower()}";
        switch (option)
        {
          case "upload-docs":
            // Here we upload the file to the specified Docs Folder
            await upload_to_docs.UploadFile(filePath, projectId, folderId, objectKey);
            Console.WriteLine("Upload Complete!");
            break;
          case "upload-bucket":
            // Here we upload the file to the specified bucket
            await upload_to_bucket.UploadFile(filePath, bucketKey, objectKey);
            Console.WriteLine("Upload Complete!");
            break;
          case "download-bucket":
            // Here we download the file from the specified bucket
            await download_from_bucket.DownloadFile(filePath, bucketKey, objectKey);
            Console.WriteLine("Download Complete!");
            break;
          case "download-docs":
            Console.WriteLine("Download from docs isn't covered in this sample!");
            break;
          default:
            Console.WriteLine("Please specify the required arguments in format 'dotnet run filePath (download or upload) objectKey (bucket or docs)'");
            break;
        }
      }
      catch (Exception ex)
      {
        System.Console.WriteLine(ex.Message);
        System.Console.WriteLine(ex.StackTrace);
      }
    }
  }
}