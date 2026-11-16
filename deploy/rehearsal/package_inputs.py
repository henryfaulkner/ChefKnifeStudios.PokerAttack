"""Package exact immutable rehearsal source/images/client; no registry publication."""
import gzip
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

ROOT=Path(__file__).parents[2]
OUTPUT=ROOT/'artifacts/H02-replica'


def compress_command(command,path,cwd=ROOT):
    # Stream complete output instead of holding a multi-image archive in memory.
    with path.open('xb') as stream,gzip.GzipFile(filename='',mode='wb',fileobj=stream,mtime=0) as compressed:
        process=subprocess.Popen(command,cwd=cwd,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
        shutil.copyfileobj(process.stdout,compressed,1024*1024)
        error=process.stderr.read();code=process.wait()
        if code:raise RuntimeError('Rehearsal archive command failed')


def main():
    snapshot=json.loads((OUTPUT/'source-snapshot.json').read_bytes());source=snapshot['source_sha'];directory=Path(snapshot['source_directory'])
    node=(ROOT/'artifacts/H01-focused/node22-path.txt').read_text(encoding='utf-8')
    artifact=OUTPUT/'client-artifact'
    result=subprocess.run([node,str(directory/'deploy/prepare-production-client.mjs'),str(OUTPUT/'client-raw/wwwroot'),str(artifact)],capture_output=True)
    (OUTPUT/'client-preparation.log').write_bytes(result.stdout+result.stderr)
    if result.returncode:raise ValueError('Exact client preparation failed')
    sys.path.insert(0,str(directory/'deploy/release'))
    from client_artifacts import pack,finalize,identity_summary
    from review import canonical
    archive=OUTPUT/'client.tar.gz'
    # Explicitly fictional run/artifact identities for the isolated H02 fixture,
    # never presented as real GitHub artifacts or uploaded to production.
    manifest=finalize(pack(artifact,archive,source,42424242424242,1),42424242424243)
    (OUTPUT/'client-manifest.json').write_bytes(canonical(manifest))
    (OUTPUT/'client-identity.json').write_bytes(canonical(identity_summary(manifest)))
    compress_command(['git','archive','--format=tar',source],OUTPUT/'source.tar.gz')
    images=['pokerattack-api:rehearsal-'+source,'pokerattack-migrations:rehearsal-'+source,
        'chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi:250b03496c69e33a206e294cbad861a94fa6844a',
        'chefknife.azurecr.io/chefknifestudios.pokerattack.server.data:250b03496c69e33a206e294cbad861a94fa6844a']
    inspected=json.loads(subprocess.check_output(['docker','image','inspect',*images]))
    identities=[{'reference':reference,'image_id':image['Id'],'source_revision':image['Config'].get('Labels',{}).get('org.opencontainers.image.revision'),
                 'repo_digests':image.get('RepoDigests',[])} for reference,image in zip(images,inspected)]
    for image in identities[:2]:
        if image['source_revision']!=source:raise ValueError('Candidate source revision differs')
    compress_command(['docker','save',*images],OUTPUT/'images.tar.gz')
    artifacts={name:{'sha256':hashlib.file_digest((OUTPUT/name).open('rb'),'sha256').hexdigest(),'bytes':(OUTPUT/name).stat().st_size}
        for name in ('source.tar.gz','images.tar.gz','client.tar.gz','client-manifest.json','client-identity.json')}
    (OUTPUT/'inputs.json').write_text(json.dumps({'source_sha':source,'images':identities,'artifacts':artifacts,
        'fixture_workflow_identity':True,'published_to_production':False},indent=2)+'\n',encoding='utf-8')
    print('Exact rehearsal inputs packaged; no image rebuild or production publication.')


if __name__=='__main__':main()
