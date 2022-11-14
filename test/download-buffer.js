const fs = require('fs');
const { BinaryTransferClient } = require('..');

async function downloadBuffer(filePath, bucketKey, objectKey, accessToken) {
    const client = new BinaryTransferClient(accessToken);
    const buffer = await client.downloadObject(bucketKey, objectKey);
    fs.writeFileSync(filePath, buffer);
}

if (process.argv.length < 6) {
    console.log('Usage:');
    console.log('node ' + __filename + ' <path to local file> <bucket key> <object key> <access token>');
    process.exit(0);
}

downloadBuffer(process.argv[2], process.argv[3], process.argv[4], process.argv[5])
    .then(_ => 'Done!')
    .catch(err => console.error(err));