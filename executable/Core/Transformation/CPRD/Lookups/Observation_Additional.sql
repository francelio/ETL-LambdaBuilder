﻿{base},
Standard as (
SELECT distinct SOURCE_CODE, TARGET_CONCEPT_ID, TARGET_DOMAIN_ID, SOURCE_VALID_START_DATE, SOURCE_VALID_END_DATE
FROM Source_to_Standard
WHERE lower(SOURCE_VOCABULARY_ID)='jnj_cprd_et_loinc' AND lower(TARGET_DOMAIN_ID)='observation' AND (TARGET_INVALID_REASON is NULL or TARGET_INVALID_REASON = '')
)

select distinct Standard.*
from Standard