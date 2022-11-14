using System.IO;
using System.Threading.Tasks;
using Autodesk.Forge;

namespace Forge_Upload_DirectToS3.test
{
  public class download_from_bucket
  {
    public static async Task<dynamic> DownloadFile(string filePath, string bucketKey, string objectKey)
    {
      BinarytransferClient.CREDENTIAL = await BinarytransferClient.Get2LeggedTokenAsync(new Scope[] { Scope.DataRead, Scope.DataWrite, Scope.DataCreate });

      dynamic response = new System.Dynamic.ExpandoObject();
      response.Status = "Download started!";

      System.Console.WriteLine(response.Status);

      byte[] downloadedBuffer = await BinarytransferClient.DownloadFromBucket(bucketKey, objectKey, null);

      await File.WriteAllBytesAsync(filePath, downloadedBuffer);

      response.Status = "Download Complete!";

      return response;
    }
  }
}