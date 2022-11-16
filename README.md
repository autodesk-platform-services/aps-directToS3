# Aps-directToS3

![Platforms](https://img.shields.io/badge/platform-Windows|MacOS-lightgray.svg)
![.NET](https://img.shields.io/badge/.NET%20Core-3.1-blue.svg)

[![oAuth2](https://img.shields.io/badge/oAuth2-v1-green.svg)](http://developer.autodesk.com/)
[![Data-Management](https://img.shields.io/badge/Data%20Management-v2-green.svg)](http://developer.autodesk.com/)
[![BIM360](https://img.shields.io/badge/BIM360-v1-green.svg)](http://developer.autodesk.com/)
[![ACC](https://img.shields.io/badge/ACC-v1-green.svg)](http://developer.autodesk.com/)

# Description

.NET sample fot new OSS upload method

# Setup

## Running locally

- clone this repository or download
- restore the packages (dotnet restore)
- prepare an OSS bucket to upload your files to
- replace the variable values at appsettings.json with your own

```json
{
  "Aps_Client_Id": "YOUR CLIENT ID",
  "Aps_Client_Secret": "YOUR CLIENT SECRET",
  "BucketKey": "YOUR BUCKET KEY",
  "ProjectId": "ID OF YOUR PROJECT PREFIXED WITH B.",
  "Folder_Urn": "YOUR FOLDER URN"
}
```

- run the commands below at cs project level to upload your local files
- you can upload to a bucket or to a folder by the second argument passed

### Uploading local file to OSS bucket (through FileStream)

`dotnet run <path to local file> upload objectKey bucket`

### Uploading local file to Docs Folder (through FileStream)

`dotnet run <path to local file> upload objectKey docs`

### Downloading file from OSS bucket (receiving the entire file into memory first)

`dotnet run <path to local file> download objectKey bucket`

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