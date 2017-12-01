cd ../cryptokuma-marketing-io

aws s3 cp --recursive wwwroot "s3://cryptokuma-marketing-web" --profile finexus

aws cloudfront create-invalidation --distribution-id E3V14KBUG8N9E8 --paths /index.html /assets/* --profile finexus

cd ../devops