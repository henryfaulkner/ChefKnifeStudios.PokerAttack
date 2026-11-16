"""Unpublished immutable rehearsal source, without altering branch or real index."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import sys
import uuid

ROOT=Path(__file__).parents[2]


def main():
    output=ROOT/'artifacts/H02-replica';output.mkdir(parents=True,exist_ok=True)
    prior=output/'source-snapshot.json'
    if prior.exists():
        if sys.argv[1:]!=['--new-attempt']:raise ValueError('Inspect existing source snapshot instead of silently replacing it')
        old=json.loads(prior.read_bytes())
        prior.rename(output/('source-snapshot-attempt-'+old['source_sha'][:12]+'.json'))
    def git(*args,env=None):return subprocess.check_output(['git',*args],cwd=ROOT,env=env).decode().strip()
    head=git('rev-parse','HEAD');branch=git('branch','--show-current')
    index=Path(git('rev-parse','--git-path','index')).resolve();before=hashlib.sha256(index.read_bytes()).hexdigest()
    with tempfile.TemporaryDirectory(prefix='rehearsal-index-',dir=output) as directory:
        env={**os.environ,'GIT_INDEX_FILE':str(Path(directory)/'index')}
        git('read-tree',head,env=env)
        # These are the authorized implementation inputs. User skill directories
        # and original untracked planning documents remain outside this snapshot.
        git('add','--','.gitattributes','.gitignore','.github','deploy','src','tests',
            'docs/deployment-evidence/G01-live-monitoring-shape.json',env=env)
        tree=git('write-tree',env=env)
        commit=git('commit-tree',tree,'-p',head,'-m','Unpublished isolated deployment-automation rehearsal snapshot',env=env)
    ref='refs/pokerattack-rehearsal/'+uuid.uuid4().hex
    git('update-ref',ref,commit)
    path=output/('source-'+commit[:12])
    git('worktree','add','--detach',str(path),commit)
    for required in ('deploy/release/server_finish.py','deploy/release/server_verify.py',
        'src/ChefKnifeStudios.PokerAttack.Shared/DTOs/Release/OwnedReleaseSessionsDTO.cs',
        'tests/release/run.cjs','deploy/monitoring/linode-docker-dashboard.json'):
        if not (path/required).is_file():raise ValueError('Required implementation input was omitted from immutable source: '+required)
    if git('rev-parse','HEAD')!=head or git('branch','--show-current')!=branch or hashlib.sha256(index.read_bytes()).hexdigest()!=before:
        raise ValueError('Active branch/index unexpectedly changed')
    result={'timestamp_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'source_sha':commit,'tree':tree,
        'base_sha':head,'local_ref':ref,'source_directory':str(path),'published':False,
        'active_branch':branch,'active_branch_and_index_unchanged':True,
        'scope':'Immutable isolated rehearsal source only; no push/merge or planning/user skill changes'}
    (output/'source-snapshot.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print('Unpublished rehearsal source:',commit)


if __name__=='__main__':main()
