const fs = require('fs');
const { BinaryTransferClient } = require('..');

async function uploadStream(filePath, bucketKey, objectKey, accessToken) {
    const client = new BinaryTransferClient(accessToken);
    const stream = fs.createReadStream(filePath);
    const object = await client.uploadObjectStream(bucketKey, objectKey, stream);
    return object;
}

if (process.argv.length < 6) {
    console.log('Usage:');
    console.log('node ' + __filename + ' <path to local file> <bucket key> <object key> <access token>');
    process.exit(0);
}

uploadStream(process.argv[2], process.argv[3], process.argv[4], process.argv[5])
    .then(obj => console.log(obj))
    .catch(err => console.error(err));