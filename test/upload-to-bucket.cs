using System.IO;
using System.Threading.Tasks;

namespace Forge_Upload_DirectToS3.test
{
  public class upload_to_bucket
  {
    public static async Task<dynamic> UploadFile(string filePath, string bucketKey, string objectKey)
    {
      FileStream fileStream = new FileStream(filePath, FileMode.Open);

      var response = await BinarytransferClient.UploadToBucket(bucketKey, objectKey, fileStream);

      return response;
    }
  }
}