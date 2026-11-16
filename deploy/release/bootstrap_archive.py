"""Provisional root-bound client for first verification, never fake acceptance.

The new client cannot be accepted until real gameplay passes, but that gameplay
must use the new client. Only the separately authenticated root bootstrap channel
can supply this adapter; ordinary CI continues to use the accepted Blob catalog.
After fresh imported/revoked proof, accept into that real catalog before promotion
and reopening. No evidence booleans or timestamps are created here.
"""
import hashlib
from pathlib import Path

from client_artifacts import check_archive,identity_summary
from client_archive import record
from manifests import validate_document
from review import canonical


class ProvisionalClient:
    def __init__(self,manifest,archive,operator):
        self.manifest=validate_document('client-manifest',manifest)
        self.archive=Path(archive);check_archive(self.archive,self.manifest)
        self.operator=operator

    def current(self,acceptance=False):
        status=self.operator('client-acceptance' if acceptance else 'client-status')
        if (status.get('format')!='pokerattack-bootstrap-client-status-v1' or status.get('client')!=identity_summary(self.manifest)
                or status.get('verified_for_acceptance') is not acceptance):
            raise ValueError('Root bootstrap publication is not this exact client')
        if acceptance and not isinstance(status.get('evidence_sha256'),str):raise ValueError('Imported proof missing')
        return status

    def pointers(self):
        status=self.current()
        etag=hashlib.sha256(canonical({key:status[key] for key in ('publication_sha256','client','pair','source_sha')})).hexdigest()
        return {'format':'pokerattack-bootstrap-provisional-client-v1','current':record(self.manifest),'previous':None},etag

    def accepted_manifest(self,run,attempt,artifact,digest):
        # Interface name is shared with the normal archive, but the pointer
        # format and generated verification reference explicitly say provisional.
        identity=record(self.manifest)
        if (run,attempt,artifact,digest)!=(identity['run_id'],identity['run_attempt'],identity['artifact_id'],identity['archive_sha256']):
            raise ValueError('Provisional client identity differs')
        self.current();return self.manifest

    def accept_verified(self,catalog,run,attempt):
        self.current(acceptance=True)
        previous=catalog.lease_check
        def held(owner_run,owner_attempt):
            if (owner_run,owner_attempt)!=(run,attempt):return False
            self.current(acceptance=True);return True
        catalog.lease_check=held
        try:return catalog.accept(self.manifest,self.archive,run,attempt)
        finally:catalog.lease_check=previous
