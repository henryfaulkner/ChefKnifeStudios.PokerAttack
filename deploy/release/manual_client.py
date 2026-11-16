"""Dispatch governed delivery; manual callers share workflow lease controls."""
import argparse
import json
import re
import subprocess
import sys

REPOSITORY='henryfaulkner/ChefKnifeStudios.PokerAttack'


def command(arguments):
    result=subprocess.run(['gh',*arguments],capture_output=True,timeout=60)
    if result.returncode:raise ValueError('GitHub operation failed; no publication attempted by this helper')
    return result.stdout


def dispatch(operation,selection=None,runner=command):
    if operation not in ('release','restore'):raise ValueError('Use governed release or accepted-artifact restore')
    arguments=['workflow','run','client.yml','--repo',REPOSITORY,'--ref','main','-f','operation='+operation]
    if operation=='restore':
        if not isinstance(selection,dict) or set(selection)!={'run','attempt','artifact','checksum'}:raise ValueError('Exact accepted restore identity required')
        for name in ('run','attempt','artifact'):
            if type(selection[name]) is not int or not 1<=selection[name]<=9007199254740991:raise ValueError('Invalid artifact identity')
        if not isinstance(selection['checksum'],str) or not re.fullmatch(r'[a-f0-9]{64}',selection['checksum']):raise ValueError('Invalid archive checksum')
        arguments.extend(value for name in ('run','attempt','artifact','checksum') for value in ('-f','restore_'+name+'='+str(selection[name])))
    elif selection is not None:raise ValueError('Release builds main; local directories are not publication inputs')
    repository=json.loads(runner(['api','repos/'+REPOSITORY]))
    if repository.get('full_name')!=REPOSITORY or repository.get('default_branch')!='main':raise ValueError('Unexpected repository/default branch')
    enabled=json.loads(runner(['api','repos/'+REPOSITORY+'/actions/variables/DEPLOYMENT_AUTOMATION_ENABLED']))
    if enabled.get('value')!='true':raise ValueError('Production automation remains disabled; complete recorded bootstrap first')
    workflow=json.loads(runner(['api','repos/'+REPOSITORY+'/actions/workflows/client.yml']))
    if workflow.get('path')!='.github/workflows/client.yml' or workflow.get('state')!='active':raise ValueError('Reviewed client workflow is not active')
    runner(arguments)
    return {'operation':operation,'workflow':'.github/workflows/client.yml','ref':'main','repository':REPOSITORY,
            'publication':'dispatched; workflow validation, host lease and browser verification still determine outcome'}


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    sub=parser.add_subparsers(dest='operation',required=True)
    sub.add_parser('release',help='Build and release main through the protected workflow')
    restore=sub.add_parser('restore',help='Restore an exact previously accepted archive through the protected workflow')
    for name in ('run','attempt','artifact'):restore.add_argument('--'+name,type=int,required=True)
    restore.add_argument('--checksum',required=True)
    args=parser.parse_args();selection={name:getattr(args,name) for name in ('run','attempt','artifact','checksum')} if args.operation=='restore' else None
    print(json.dumps(dispatch(args.operation,selection)))


def cli():
    try:main()
    except (ValueError,subprocess.TimeoutExpired):sys.exit('Client publication blocked; inspect checklist/bootstrap or GitHub run state before retrying.')


if __name__=='__main__':cli()
