"""Add only the Docker textfile directory/namespace to the inspected pipeline.

Never print configuration or copy credentials to the repository. The installer
uses protected host-side paths and validates a staged copy before replacement.
"""
import re

EXPORTER='prometheus.exporter.unix "integrations_node_exporter"'
RELABEL='prometheus.relabel "integrations_node_exporter"'
TEXTFILE='\n  textfile {\n    directory = "/var/lib/linode-docker-metrics"\n  }\n'
FORWARD='otelcol.receiver.prometheus.integrations_node_exporter.receiver'


def component(raw,header):
    matches=list(re.finditer(r'(?m)^'+re.escape(header)+r'\s*\{',raw))
    if len(matches)!=1:raise ValueError('Expected one inspected component')
    start=matches[0].end();depth=1;position=start;quoted=False;escaped=False;comment=False
    while position<len(raw):
        character=raw[position]
        if comment:
            if character=='\n':comment=False
        elif quoted:
            if escaped:escaped=False
            elif character=='\\':escaped=True
            elif character=='"':quoted=False
        elif raw[position:position+2]=='//':comment=True;position+=1
        elif character=='"':quoted=True
        elif character=='{':depth+=1
        elif character=='}':
            depth-=1
            if depth==0:return start,position
        position+=1
    raise ValueError('Unterminated Alloy component')


def prepare(raw):
    start,end=component(raw,EXPORTER);body=raw[start:end]
    if re.search(r'\btextfile\s*\{',body) or re.search(r'"textfile"',body):
        raise ValueError('Existing textfile configuration requires reconciliation')
    raw=raw[:end]+TEXTFILE+raw[end:]
    start,end=component(raw,RELABEL);body=raw[start:end]
    if FORWARD not in body or 'action        = "keep"' not in body:
        raise ValueError('Unexpected node forwarding/allowlist shape')
    matches=list(re.finditer(r'(?m)^(\s*regex\s*=\s*)"([^"\n]+)"',body))
    if len(matches)!=1 or 'linode_docker_' in body:raise ValueError('Ambiguous or already extended metric allowlist')
    match=matches[0];existing=match[2]
    if not all(name in existing.split('|') for name in ('up','node_uname_info','node_textfile_scrape_error')):
        raise ValueError('Inspected host metric allowlist no longer matches')
    body=body[:match.start(2)]+existing+'|linode_docker_.*'+body[match.end(2):]
    return raw[:start]+body+raw[end:]


def rollback(raw):
    start,end=component(raw,EXPORTER);body=raw[start:end]
    if body.count(TEXTFILE)!=1:raise ValueError('Docker textfile addition changed')
    body=body.replace(TEXTFILE,'',1);raw=raw[:start]+body+raw[end:]
    start,end=component(raw,RELABEL);body=raw[start:end]
    if body.count('|linode_docker_.*')!=1:raise ValueError('Docker allowlist addition changed')
    return raw[:start]+body.replace('|linode_docker_.*','',1)+raw[end:]
