const fs = require('fs');
const { BinaryTransferClient } = require('..');

async function downloadStream(filePath, bucketKey, objectKey, accessToken) {
    const client = new BinaryTransferClient(accessToken);
    const stream = await client.downloadObjectStream(bucketKey, objectKey);
    stream.pipe(fs.createWriteStream(filePath));
}

if (process.argv.length < 6) {
    console.log('Usage:');
    console.log('node ' + __filename + ' <path to local file> <bucket key> <object key> <access token>');
    process.exit(0);
}

downloadStream(process.argv[2], process.argv[3], process.argv[4], process.argv[5]);