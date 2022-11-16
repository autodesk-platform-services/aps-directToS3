# APS-directToS3

This repo contains samples on Node.js and .NET for the new binary transfer in Autodesk Platform Services.

You can refer to the specific branch of your preference to start taking advantage of it.

You can refer to [.NET Core 3.1](https://github.com/autodesk-platform-services/aps-directToS3/tree/netcoreapp3.1), [.NET 6](https://github.com/autodesk-platform-services/aps-directToS3/tree/net6.0) (both differ on csproj and program.cs due to implicit usings) or [Node.js](https://github.com/autodesk-platform-services/aps-directToS3/tree/node) samples.

## How does it work?

Here's a pseudo-code explaining how the new upload and download can be implemented:

### Upload

1. Calculate the number of parts of the file to upload
   - Note: each uploaded part except for the last one must be at least 5MB
2. Generate up to 25 URLs for uploading specific parts of the file using the `GET buckets/:bucketKey/objects/:objectKey/signeds3upload?firstPart=<index of first part>&parts=<number of parts>` endpoint
   - The part numbers start with 1
   - For example, to generate upload URLs for parts 10 through 15, set `firstPart` to 10 and `parts` to 6
   - This endpoint also returns an `uploadKey` that is used later to request additional URLs or to finalize the upload
3. Upload remaining parts of the file to their corresponding upload URLs
   - Consider retrying (for example, with an exponential backoff) individual uploads when the response code is 100-199, 429, or 500-599
   - If the response code is 403, the upload URLs have expired; go back to step #2
   - If you've used up all the upload URLs and there are still parts that must be uploaded, go back to step #2
4. Finalize the upload using the `POST buckets/:bucketKey/objects/:objectKey/signeds3upload` endpoint, using the `uploadKey` value from step #2

### Download

1. Generate a download URL using the [GET buckets/:bucketKey/objects/:objectName/signeds3download](https://forge.autodesk.com/en/docs/data/v2/reference/http/buckets-:bucketKey-objects-:objectName-signeds3download-GET) endpoint
2. Use the new URL to download the OSS object directly from AWS S3
   - Consider retrying (for example, with an exponential backoff) the download when the response code is 100-199, 429, or 500-599
