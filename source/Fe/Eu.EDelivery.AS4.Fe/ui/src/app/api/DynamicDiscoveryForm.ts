import { FormGroup, AbstractControl, Validators } from '@angular/forms';

/* tslint:disable */
import { SendingPmode } from './SendingPmode';
import { FormWrapper } from './../common/form.service';
import { DynamicDiscovery } from './DynamicDiscovery';
import { ItemType } from './ItemType';

export class DynamicDiscoveryForm {
    public static getForm(formBuilder: FormWrapper, current: DynamicDiscovery | null, runtime: ItemType[], path: string): FormWrapper {
        let isNew = !!!current;
        return formBuilder
            .group({
                [DynamicDiscovery.FIELD_smlScheme]: [formBuilder.createFieldValue(current, DynamicDiscovery.FIELD_smlScheme, path, null, runtime), Validators.required],
                [DynamicDiscovery.FIELD_smpServerDomainName]: [formBuilder.createFieldValue(current, DynamicDiscovery.FIELD_smpServerDomainName, path, null, runtime), Validators.required],
                [DynamicDiscovery.FIELD_documentIdentifier]: [formBuilder.createFieldValue(current, DynamicDiscovery.FIELD_documentIdentifier, path, null, runtime), Validators.required],
                [DynamicDiscovery.FIELD_documentIdentifierScheme]: [formBuilder.createFieldValue(current, DynamicDiscovery.FIELD_documentIdentifierScheme, path, null, runtime), Validators.required]
            })
            .onStatusChange(DynamicDiscovery.FIELD_smlScheme, (status, wrapper) => {
                if (isNew) {
                    isNew = false;

                    const sml = wrapper.form.get(DynamicDiscovery.FIELD_smlScheme);
                    const documentIdent = wrapper.form.get(DynamicDiscovery.FIELD_documentIdentifier);
                    const documentIdentScheme = wrapper.form.get(DynamicDiscovery.FIELD_documentIdentifierScheme);

                    sml!.setValue(runtime[`${path}.${DynamicDiscovery.FIELD_smlScheme}`]);
                    documentIdent!.setValue(runtime[`${path}.${DynamicDiscovery.FIELD_documentIdentifier}`]);
                    documentIdentScheme!.setValue(runtime[`${path}.${DynamicDiscovery.FIELD_documentIdentifierScheme}`]);
                }
            });
    }
}
