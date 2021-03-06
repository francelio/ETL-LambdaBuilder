﻿ {base},
Standard as (
SELECT distinct SOURCE_CODE, TARGET_CONCEPT_ID, TARGET_DOMAIN_ID, SOURCE_VALID_START_DATE, SOURCE_VALID_END_DATE
FROM Source_to_Standard
WHERE lower(SOURCE_VOCABULARY_ID) = 'jnj_optum_nlp_drug'
AND (TARGET_STANDARD_CONCEPT IS NOT NULL or TARGET_STANDARD_CONCEPT != '')
AND (TARGET_INVALID_REASON IS NULL or TARGET_INVALID_REASON = '')
)

select distinct Standard.*, NULL AS SOURCE_VOCABULARY_ID, NULL AS SOURCE_TARGET_CONCEPT_ID,	NULL AS SOURCE_VALID_START_DATE, NULL AS SOURCE_VALID_END_DATE, ingredient_level.ingredient_concept_id
from Standard
left join ingredient_level on ingredient_level.concept_id = Standard.TARGET_CONCEPT_ID