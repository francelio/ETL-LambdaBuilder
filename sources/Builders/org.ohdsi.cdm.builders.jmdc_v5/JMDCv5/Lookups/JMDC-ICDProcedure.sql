{Source_to_Standard}
SELECT DISTINCT REPLACE(REPLACE(SOURCE_CODE, '.', ''), '-', ''), TARGET_CONCEPT_ID
FROM CTE_VOCAB_MAP
WHERE lower(SOURCE_VOCABULARY_ID) IN ('icd9proc') and lower(TARGET_DOMAIN_ID) = 'procedure'