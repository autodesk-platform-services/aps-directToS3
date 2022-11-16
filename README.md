# Aps-directToS3

![Platforms](https://img.shields.io/badge/platform-Windows|MacOS-lightgray.svg)
[![Node.js](https://img.shields.io/badge/Node.js-16.14.0-blue.svg)](https://nodejs.org/)

[![oAuth2](https://img.shields.io/badge/oAuth2-v1-green.svg)](http://developer.autodesk.com/)
[![Data-Management](https://img.shields.io/badge/Data%20Management-v2-green.svg)](http://developer.autodesk.com/)
[![BIM360](https://img.shields.io/badge/BIM360-v1-green.svg)](http://developer.autodesk.com/)
[![ACC](https://img.shields.io/badge/ACC-v1-green.svg)](http://developer.autodesk.com/)

# Description

Node.js utility for the new binary transfer in Autodesk Platform Services.

## Running locally

- clone this repository, and `cd` to the project folder in terminal
- install Node.js dependencies: `yarn install`
- prepare an OSS bucket to upload/download your files to/from
- generate a APS access token with `data:read`, `data:write`, and `data:create` scopes
- run any of the scripts in the _test_ folder, for example:

### Uploading local file to OSS bucket (loading the entire file into memory first)

`node test/upload-buffer.js <path to local file> <bucket key> <object key> <access token>`

### Uploading local file to OSS bucket (as a stream)

`node test/upload-stream.js <path to local file> <bucket key> <object key> <access token>`

### Downloading OSS object to local file (receiving the entire file into memory first)

`node test/download-buffer.js <path to local file> <bucket key> <object key> <access token>`

### Downloading OSS object to local file (as a stream)

`node test/download-stream.js <path to local file> <bucket key> <object key> <access token>`

### Uploading local file to a Data Management hub (such as BIM 360, Fusion Teams, or ACC)

`node test/upload-to-hub.js <path to local file> <project id> <folder id> <access token>`

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