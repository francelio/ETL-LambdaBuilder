﻿using System;
using System.Collections.Generic;
using System.Linq;
using org.ohdsi.cdm.framework.core.Base;
using org.ohdsi.cdm.framework.entities.Omop;

namespace org.ohdsi.cdm.builders.jmdc_v5
{
    public class Jmdcv5PersonBuilder : PersonBuilder
    {
        readonly Dictionary<long, DateTime> visitsDate = new Dictionary<long, DateTime>();
        readonly Dictionary<long, DateTime> visitsDateDiagnosis = new Dictionary<long, DateTime>();
        readonly Dictionary<long, VisitOccurrence> pharmacyVisits = new Dictionary<long, VisitOccurrence>();

        private void SetStartDate(IDictionary<long, VisitOccurrence> visitOccurrences, IEntity entity)
        {
            if (entity.StartDate == DateTime.MinValue && entity.VisitOccurrenceId.HasValue)
            {
                if (visitOccurrences.ContainsKey(entity.VisitOccurrenceId.Value))
                {
                    entity.StartDate = visitOccurrences[entity.VisitOccurrenceId.Value].StartDate;
                }
                else if (pharmacyVisits.ContainsKey(entity.VisitOccurrenceId.Value))
                {
                    entity.StartDate = pharmacyVisits[entity.VisitOccurrenceId.Value].StartDate;
                    entity.VisitOccurrenceId = null;
                }
            }
        }

        public override IEnumerable<VisitOccurrence> BuildVisitOccurrences(VisitOccurrence[] visitOccurrences,
            ObservationPeriod[] observationPeriods)
        {
            foreach (var visitOccurrence in visitOccurrences)
            {
                if (visitsDate.ContainsKey(visitOccurrence.Id))
                {
                    visitOccurrence.StartDate = visitsDate[visitOccurrence.Id];
                }
                else if (visitsDateDiagnosis.ContainsKey(visitOccurrence.Id))
                {
                    if (visitOccurrence.StartDate.Year == visitsDateDiagnosis[visitOccurrence.Id].Year &&
                        visitOccurrence.StartDate.Month == visitsDateDiagnosis[visitOccurrence.Id].Month)
                    {
                        visitOccurrence.StartDate = visitsDateDiagnosis[visitOccurrence.Id];
                    }
                }

                var daysValue = visitOccurrence.AdditionalFields["num_of_days"];
                var days = string.IsNullOrEmpty(daysValue) ? 0 : int.Parse(daysValue);

                visitOccurrence.EndDate = visitOccurrence.StartDate;

                if (days > 0)
                {
                    visitOccurrence.EndDate = visitOccurrence.StartDate.AddDays(days - 1);
                }
            }

            foreach (var visitOccurrence in base.BuildVisitOccurrences(visitOccurrences, observationPeriods))
            {
                if (visitOccurrence.SourceValue.ToLower() == "pharmacy")
                {
                    pharmacyVisits.Add(visitOccurrence.Id, visitOccurrence);
                    continue;
                }

                yield return visitOccurrence;
            }
        }

        public override Death BuildDeath(Death[] death, Dictionary<long, VisitOccurrence> visitOccurrences,
            ObservationPeriod[] observationPeriods)
        {
            foreach (var d in death)
            {
                if (d.TypeConceptId == 38003567 && d.VisitOccurrenceId.HasValue &&
                    visitOccurrences.ContainsKey(d.VisitOccurrenceId.Value))
                {
                    d.StartDate = visitOccurrences[d.VisitOccurrenceId.Value].EndDate.Value;
                }
            }

            return base.BuildDeath(death, visitOccurrences, observationPeriods);
        }

        private IEnumerable<ConditionOccurrence> CleanupConditions(
            IEnumerable<ConditionOccurrence> conditionOccurrences)
        {
            foreach (var co in conditionOccurrences)
            {
                // This flag indicates that a test is being order to determine whether indeed the diagnosis is correct. 
                // A later record should confirm if it is, and the [month and year of start of medical care] will inform us what the right date of onset is, as already implements
                if (co.AdditionalFields != null && co.AdditionalFields.ContainsKey("suspicion_flag") &&
                    co.AdditionalFields["suspicion_flag"] == "1")
                    continue;

                yield return co;
            }
        }

        /*
          *	Projects Enumeration of ConditionOccurrence from the raw set of ConditionOccurrence entities. 
          *	During build:
          *		override the condition's start date using the end date of the corresponding visit.
          *		overide TypeConceptId per CDM Mapping spec. 
          *	
          */
        public override IEnumerable<ConditionOccurrence> BuildConditionOccurrences(
            ConditionOccurrence[] conditionOccurrences, Dictionary<long, VisitOccurrence> visitOccurrences,
            ObservationPeriod[] observationPeriods)
        {
            foreach (var co in conditionOccurrences)
            {
                //DateTime dateOfMedicalCare;
                if (co.StartDate == DateTime.MinValue && co.VisitOccurrenceId.HasValue &&
                    visitOccurrences.ContainsKey(co.VisitOccurrenceId.Value))
                    co.StartDate = visitOccurrences[co.VisitOccurrenceId.Value].StartDate;

                //else if (!string.IsNullOrEmpty(co.AdditionalFields["month_and_year"]) && DateTime.TryParseExact(co.AdditionalFields["month_and_year"] + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dateOfMedicalCare) && co.StartDate < dateOfMedicalCare)
                //   co.StartDate = dateOfMedicalCare;

                co.EndDate = null;
            }

            var conditions =
                CleanupConditions(base.BuildEntities(conditionOccurrences, visitOccurrences, observationPeriods))
                    .ToArray();

            foreach (var sameProviderId in conditions.GroupBy(c => c.ProviderId))
            {
                foreach (var sameSourceValue in sameProviderId.GroupBy(c => c.SourceValue))
                {
                    foreach (var episode in sameSourceValue.GroupBy(c => c.AdditionalFields["month_and_year_of_start"]))
                    {
                        var parentCondition = episode.FirstOrDefault();

                        if (parentCondition == null ||
                            string.IsNullOrEmpty(parentCondition.AdditionalFields["start_m_and_y_date"])) continue;
                        if (!parentCondition.VisitOccurrenceId.HasValue ||
                            !visitOccurrences.ContainsKey(parentCondition.VisitOccurrenceId.Value)) continue;

                        var endDate = episode.Max(c => c.StartDate);
                        var startOfMedicalCare = DateTime.Parse(parentCondition.AdditionalFields["start_m_and_y_date"]);

                        var newCondition = new ConditionOccurrence(parentCondition);
                        newCondition.Id = chunkData.KeyMasterOffset.ConditionOccurrenceId;
                        newCondition.VisitOccurrenceId = null;
                        newCondition.TypeConceptId = 38000246;
                        newCondition.EndDate = endDate;
                        newCondition.StartDate = startOfMedicalCare;

                        if (startOfMedicalCare <
                            observationPeriods[0].StartDate)
                        {
                            newCondition.StartDate = observationPeriods[0].StartDate;
                        }

                        if (newCondition.StartDate > DateTime.MinValue && newCondition.StartDate < newCondition.EndDate)
                        {
                            yield return newCondition;
                        }
                    }
                }
            }

            foreach (var co in conditions)
            {
                yield return co;
            }
        }


        /*
         *	Projects Enumeration of ProcedureOccurrence from the raw set of ProcedureOccurence entities. 
         *	During build:
         *		override the procedure's start date using the end date of the corresponding visit.
         *		overide TypeConceptId per CDM Mapping spec. 
         *	
         */
        public override IEnumerable<ProcedureOccurrence> BuildProcedureOccurrences(
            ProcedureOccurrence[] procedureOccurrences, Dictionary<long, VisitOccurrence> visitOccurrences,
            ObservationPeriod[] observationPeriods)
        {
            foreach (var po in procedureOccurrences)
            {
                SetStartDate(visitOccurrences, po);
            }

            return base.BuildEntities(procedureOccurrences, visitOccurrences, observationPeriods);
        }

        /*
          *	Projects Enumeration of ConditionOccurrence from the raw set of ConditionOccurrence entities. 
          *	During build:
          *		override the condition's start date using the end date of the corresponding visit.
          *		overide TypeConceptId per CDM Mapping spec. 
          *	
          */
        public override IEnumerable<DrugExposure> BuildDrugExposures(DrugExposure[] drugExposures,
            Dictionary<long, VisitOccurrence> visitOccurrences, ObservationPeriod[] observationPeriods)
        {
            foreach (var de in drugExposures)
            {
                SetStartDate(visitOccurrences, de);

                de.EndDate = de.StartDate.AddDays(de.DaysSupply.HasValue ? de.DaysSupply.Value : 0);
            }

            return base.BuildEntities(drugExposures, visitOccurrences, observationPeriods);
        }

        public override IEnumerable<VisitCost> BuildVisitCosts(VisitOccurrence[] visitOccurrences)
        {
            foreach (var c in base.BuildVisitCosts(visitOccurrences).Where(vc => vc.TotalPaid.HasValue))
            {
                chunkData.AddCostData(new Cost
                {
                    CostId = chunkData.KeyMasterOffset.VisitCostId,
                    CurrencyConceptId = c.CurrencyConceptId,
                    TypeId = 5031,
                    Domain = "Visit",
                    EventId = c.Id,
                    TotalPaid = c.TotalPaid
                });

                yield return c;
            }
        }

        public override IEnumerable<DrugCost> BuildDrugCosts(DrugExposure[] drugExposures)
        {
            foreach (var c in base.BuildDrugCosts(drugExposures))
            {
                chunkData.AddCostData(new Cost
                {
                    CostId = chunkData.KeyMasterOffset.VisitCostId,
                    CurrencyConceptId = c.CurrencyConceptId,
                    TypeId = 5032,
                    Domain = "Drug",
                    EventId = c.Id,
                    TotalPaid = c.TotalPaid,
                    TotalCharge = c.PaidByCoordinationBenefits
                });

                yield return c;
            }
        }

        public override IEnumerable<ProcedureCost> BuildProcedureCosts(ProcedureOccurrence[] procedureOccurrences)
        {
            foreach (var c in base.BuildProcedureCosts(procedureOccurrences))
            {
                chunkData.AddCostData(new Cost
                {
                    CostId = chunkData.KeyMasterOffset.VisitCostId,
                    CurrencyConceptId = c.CurrencyConceptId,
                    TypeId = 5032,
                    Domain = "Procedure",
                    EventId = c.Id,
                    TotalPaid = c.TotalPaid,
                    TotalCharge = c.PaidByCoordinationBenefits
                });

                yield return c;
            }
        }

        public override void Build(Dictionary<string, long> providers)
        {
            //"If the claim is associated with a diagnosis, " +
            //"and (1) that [Diagnosis].[Month and year of start of medical care] date falls within the [Month and year of medical care] and (2) " +
            //"there is no other diagnoses with the same level 4 ICD-10 code from the same institution for the same member with the same [Month and year of start of medical care] date, " +
            //"then the [Month and year of start of medical care] date is used as the visit_start_date."
            foreach (var sameSource in conditionOccurrencesRaw
                .Where(c => !string.IsNullOrEmpty(c.AdditionalFields["start_m_and_y_date"]))
                .GroupBy(c => c.SourceValue))
            {
                foreach (var sameInstitution in sameSource.GroupBy(c => c.ProviderId))
                {
                    foreach (var sameStartOfMedicalCare in sameInstitution.GroupBy(c =>
                        c.AdditionalFields["month_and_year_of_start"]))
                    {
                        var diagnosis = sameStartOfMedicalCare.ToList();
                        if (diagnosis.Count == 1)
                        {
                            if (!visitsDateDiagnosis.ContainsKey(diagnosis[0].VisitOccurrenceId.Value))
                            {
                                visitsDateDiagnosis.Add(diagnosis[0].VisitOccurrenceId.Value,
                                    DateTime.Parse(diagnosis[0].AdditionalFields["start_m_and_y_date"]));
                            }
                            else
                            {
                                var newValue = DateTime.Parse(diagnosis[0].AdditionalFields["start_m_and_y_date"]);

                                if (visitsDateDiagnosis[diagnosis[0].VisitOccurrenceId.Value] < newValue)
                                    visitsDateDiagnosis[diagnosis[0].VisitOccurrenceId.Value] = newValue;
                            }
                        }
                    }
                }
            }


            //If the claim is associated with a Drug.[Prescription date] or  Procedure.[Procedure date], use the minimum of those dates as the visit_start_date.
            foreach (var p in procedureOccurrencesRaw.GroupBy(p => p.VisitOccurrenceId))
            {
                var procedure = p.First();
                if (!procedure.VisitOccurrenceId.HasValue) continue;

                var procedures = p.Where(pp => pp.StartDate > DateTime.MinValue).ToArray();

                if (procedures.Length > 0)
                {
                    var minProcedureDate = procedures.Min(pp => pp.StartDate);

                    if (visitsDate.ContainsKey(procedure.VisitOccurrenceId.Value))
                    {
                        visitsDate[procedure.VisitOccurrenceId.Value] = minProcedureDate;
                        continue;
                    }

                    visitsDate.Add(procedure.VisitOccurrenceId.Value, minProcedureDate);
                }
            }

            foreach (var d in drugExposuresRaw.GroupBy(d => d.VisitOccurrenceId))
            {
                var drug = d.First();
                if (!drug.VisitOccurrenceId.HasValue) continue;

                var drugs = d.Where(dd => dd.StartDate > DateTime.MinValue).ToArray();

                if (drugs.Length > 0)
                {
                    var minDrugDate = drugs.Min(dd => dd.StartDate);

                    if (!visitsDate.ContainsKey(drug.VisitOccurrenceId.Value))
                    {
                        visitsDate.Add(drug.VisitOccurrenceId.Value, minDrugDate);
                        continue;
                    }

                    // [Date of prescription] < [Date of procedure]
                    // minDrugDate < visitsDate[drug.VisitOccurrenceId.Value]
                    if (minDrugDate < visitsDate[drug.VisitOccurrenceId.Value])
                    {
                        visitsDate[drug.VisitOccurrenceId.Value] = minDrugDate;
                    }
                }
            }

            base.Build(providers);
        }

        public override IEnumerable<Observation> BuildObservations(Observation[] observations,
            Dictionary<long, VisitOccurrence> visitOccurrences,
            ObservationPeriod[] observationPeriods)
        {
            foreach (var o in base.BuildObservations(observations, visitOccurrences, observationPeriods))
            {
                if (string.IsNullOrEmpty(o.ValueAsString) && !o.ValueAsNumber.HasValue &&
                    (!o.ValueAsConceptId.HasValue || o.ValueAsConceptId.Value == 0)) continue;

                yield return o;
            }
        }

        public override IEnumerable<Measurement> BuildMeasurement(Measurement[] measurements,
            Dictionary<long, VisitOccurrence> visitOccurrences,
            ObservationPeriod[] observationPeriods)
        {
            foreach (var measurement in measurements)
            {
                decimal valueAsNumber;

                if (decimal.TryParse(measurement.ValueSourceValue, out valueAsNumber))
                    measurement.ValueAsNumber = valueAsNumber;
            }

            return base.BuildMeasurement(measurements, visitOccurrences, observationPeriods);
        }

        public override void AddToChunk(string domain, IEnumerable<IEntity> entities)
        {
            foreach (var entity in entities)
            {
                var entityDomain = entity.Domain;
                if (string.IsNullOrEmpty(entityDomain))
                {
                    entityDomain = domain;
                }


                switch (entityDomain)
                {
                    case "Condition":
                        var obs = entity as Observation;
                        if (obs == null || obs.ValueAsNumber == 1)
                        {
                            var cond = entity as ConditionOccurrence ??
                                       new ConditionOccurrence(entity)
                                       {
                                           Id = chunkData.KeyMasterOffset.ConditionOccurrenceId
                                       };
                            conditionForEra.Add(cond);
                            chunkData.AddData(cond);
                        }

                        break;

                    case "Measurement":
                        if (entity.TypeConceptId != 38000246)
                        {
                            var mes = entity as Measurement ??
                                      new Measurement(entity)
                                      {
                                          Id = chunkData.KeyMasterOffset.MeasurementId
                                      };


                            var result = vocabulary.Lookup(mes.SourceValue,
                                @"JMDCv5\Lookups\JMDC-ICD10-MapsToValue.sql", DateTime.MinValue, false);
                            long conceptId = 4181412;

                            if (result.Any() && result[0].ConceptId.HasValue && result[0].ConceptId > 0)
                                conceptId = result[0].ConceptId.Value;

                            if (!mes.ValueAsConceptId.HasValue || mes.ValueAsConceptId == 0)
                            {
                                mes.ValueAsConceptId = conceptId;
                            }

                            chunkData.AddData(mes);
                        }

                        break;

                    case "Meas Value":
                        chunkData.AddData(entity as Measurement ??
                                          new Measurement(entity) {Id = chunkData.KeyMasterOffset.MeasurementId});
                        break;

                    case "Observation":
                        if (entity.TypeConceptId != 38000246)
                        {
                            var obser = entity as Observation ??
                                        new Observation(entity)
                                        {
                                            Id = chunkData.KeyMasterOffset.ObservationId
                                        };

                            var result = vocabulary.Lookup(obser.SourceValue,
                                @"JMDCv5\Lookups\JMDC-ICD10-MapsToValue.sql", DateTime.MinValue, false);
                            long conceptId = 4181412;

                            if (result.Any() && result[0].ConceptId.HasValue && result[0].ConceptId > 0)
                                conceptId = result[0].ConceptId.Value;

                            if (!obser.ValueAsConceptId.HasValue || obser.ValueAsConceptId == 0)
                            {
                                obser.ValueAsConceptId = conceptId;
                            }

                            chunkData.AddData(obser);
                        }

                        break;

                    case "Procedure":
                        var p = entity as ProcedureOccurrence ??
                                new ProcedureOccurrence(entity)
                                {
                                    Id =
                                        chunkData.KeyMasterOffset
                                            .ProcedureOccurrenceId
                                };

                        chunkData.AddData(p);
                        break;

                    case "Device":
                        chunkData.AddData(entity as DeviceExposure ??
                                          new DeviceExposure(entity)
                                          {
                                              Id = chunkData.KeyMasterOffset.DeviceExposureId
                                          });
                        break;

                    case "Drug":
                        var drg = entity as DrugExposure ??
                                  new DrugExposure(entity)
                                  {
                                      Id = chunkData.KeyMasterOffset.DrugExposureId,
                                      GetEraConceptIdsCall = vocabulary.LookupIngredientLevel
                                  };

                        drugForEra.Add(drg);
                        chunkData.AddData(drg);
                        break;

                }

                //HIX-823
                if (domain == "Procedure" && entityDomain != "Procedure")
                {
                    var po = (ProcedureOccurrence) entity;
                    po.ConceptId = 0;
                    chunkData.AddData(po);
                }

                if (domain == "Observation" && entityDomain != "Observation")
                {
                    var o = (Observation) entity;
                    o.ConceptId = 0;
                    chunkData.AddData(o);
                }
            }
        }
    }
}
