//! Opt-in mutation harness: reuse each existing integration suite in its own module.
//! Run serially with --features mutation-suite --lib --test mutation_suite.
//! Normal test targets and product builds do not enable this feature.
//! The inventory contract prevents silently omitting a newly added suite.

#[path = "ac_bias_reuse.rs"]
mod suite_ac_bias_reuse;
#[path = "accepted_time_grid.rs"]
mod suite_accepted_time_grid;
#[path = "adaptive.rs"]
mod suite_adaptive;
#[path = "analyses.rs"]
mod suite_analyses;
#[path = "audit_reference_contracts.rs"]
mod suite_audit_reference_contracts;
#[path = "audit_output_contracts.rs"]
mod suite_audit_output_contracts;
#[path = "analysis_audits.rs"]
mod suite_analysis_audits;
#[path = "behavioral_resistor.rs"]
mod suite_behavioral_resistor;
#[path = "behavioral_transient.rs"]
mod suite_behavioral_transient;
#[path = "behavioral_unary.rs"]
mod suite_behavioral_unary;
#[path = "behavioral_voltage.rs"]
mod suite_behavioral_voltage;
#[path = "bjt_coupled_charge.rs"]
mod suite_bjt_coupled_charge;
#[path = "bsim330.rs"]
mod suite_bsim330;
#[path = "campaign_cli.rs"]
mod suite_campaign_cli;
#[path = "configured_relative.rs"]
mod suite_configured_relative;
#[path = "consistency.rs"]
mod suite_consistency;
#[path = "continuation_hardening.rs"]
mod suite_continuation_hardening;
#[path = "continuation_networks.rs"]
mod suite_continuation_networks;
#[path = "continuation.rs"]
mod suite_continuation;
#[path = "contracts.rs"]
mod suite_contracts;
#[path = "controlled_sources.rs"]
mod suite_controlled_sources;
#[path = "csc_union_progress.rs"]
mod suite_csc_union_progress;
#[path = "dc.rs"]
mod suite_dc;
#[path = "decimal_time_roundtrip.rs"]
mod suite_decimal_time_roundtrip;
#[path = "difficult_continuation.rs"]
mod suite_difficult_continuation;
#[path = "dynamic.rs"]
mod suite_dynamic;
#[path = "edge_cases.rs"]
mod suite_edge_cases;
#[path = "endpoint.rs"]
mod suite_endpoint;
#[path = "endurance.rs"]
mod suite_endurance;
#[path = "experiment.rs"]
mod suite_experiment;
#[path = "expression_operators.rs"]
mod suite_expression_operators;
#[path = "expression.rs"]
mod suite_expression;
#[path = "faer_complex_refinement.rs"]
mod suite_faer_complex_refinement;
#[path = "faer_real_refinement.rs"]
mod suite_faer_real_refinement;
#[path = "faults.rs"]
mod suite_faults;
#[path = "fuzz_dynamic.rs"]
mod suite_fuzz_dynamic;
#[path = "fuzz_smoke.rs"]
mod suite_fuzz_smoke;
#[path = "gain_differential_transient.rs"]
mod suite_gain_differential_transient;
#[path = "hardening.rs"]
mod suite_hardening;
#[path = "import_cli.rs"]
mod suite_import_cli;
#[path = "initial_newton.rs"]
mod suite_initial_newton;
#[path = "intel_backend.rs"]
mod suite_intel_backend;
#[path = "junction_storage.rs"]
mod suite_junction_storage;
#[path = "lu_homogeneous_constraints.rs"]
mod suite_lu_homogeneous_constraints;
#[path = "lu_tiny_sensors.rs"]
mod suite_lu_tiny_sensors;
#[path = "lu_zero_constraints.rs"]
mod suite_lu_zero_constraints;
#[path = "matrix_conformance.rs"]
mod suite_matrix_conformance;
#[path = "model_charge.rs"]
mod suite_model_charge;
#[path = "modulated_sources.rs"]
mod suite_modulated_sources;
#[path = "mos1_channel.rs"]
mod suite_mos1_channel;
#[path = "mutual_inductance.rs"]
mod suite_mutual_inductance;
#[path = "ngspice_analysis_cards.rs"]
mod suite_ngspice_analysis_cards;
#[path = "ngspice_compat.rs"]
mod suite_ngspice_compat;
#[path = "ngspice_inactive_ac.rs"]
mod suite_ngspice_inactive_ac;
#[path = "ngspice_resistor_models.rs"]
mod suite_ngspice_resistor_models;
#[path = "nonlinear_trapezoidal.rs"]
mod suite_nonlinear_trapezoidal;
#[path = "nonlinear.rs"]
mod suite_nonlinear;
#[path = "overnight_survivors_20261028.rs"]
mod suite_overnight_survivors_20261028;
#[path = "overnight_survivors_20261029.rs"]
mod suite_overnight_survivors_20261029;
#[path = "overnight_survivors_20261101.rs"]
mod suite_overnight_survivors_20261101;
#[path = "overnight_survivors_20261102.rs"]
mod suite_overnight_survivors_20261102;
#[path = "overnight_survivors_20261103.rs"]
mod suite_overnight_survivors_20261103;
#[path = "overnight_survivors.rs"]
mod suite_overnight_survivors;
#[path = "properties.rs"]
mod suite_properties;
#[path = "property_campaign.rs"]
mod suite_property_campaign;
#[path = "providers.rs"]
mod suite_providers;
#[path = "pspice_switch.rs"]
mod suite_pspice_switch;
#[path = "sampling.rs"]
mod suite_sampling;
#[path = "scalar_decorations.rs"]
mod suite_scalar_decorations;
#[path = "soak_tail_contracts.rs"]
mod suite_soak_tail_contracts;
#[path = "sparse_ac_circuits.rs"]
mod suite_sparse_ac_circuits;
#[path = "sparse_ac.rs"]
mod suite_sparse_ac;
#[path = "sparse_dynamic.rs"]
mod suite_sparse_dynamic;
#[path = "sparse_qualification.rs"]
mod suite_sparse_qualification;
#[path = "sparse_transient.rs"]
mod suite_sparse_transient;
#[path = "spice_diode_continuation.rs"]
mod suite_spice_diode_continuation;
#[path = "spice_diode_oracle.rs"]
mod suite_spice_diode_oracle;
#[path = "spice_diode.rs"]
mod suite_spice_diode;
#[path = "storage_power.rs"]
mod suite_storage_power;
#[path = "switch_stagnation_20261103.rs"]
mod suite_switch_stagnation_20261103;
#[path = "transient.rs"]
mod suite_transient;
#[path = "variation.rs"]
mod suite_variation;
#[path = "voltage_scaled_cccs.rs"]
mod suite_voltage_scaled_cccs;

#[path = "nonlinear_half_step.rs"]
mod suite_nonlinear_half_step;

#[path = "backlog_survivors_20261118.rs"]
mod suite_backlog_survivors_20261118;

#[path = "imported_transient_contract.rs"]
mod suite_imported_transient_contract;

#[path = "epoch_validation_contracts.rs"]
mod suite_epoch_validation_contracts;

#[path = "epoch_api_admission.rs"]
mod suite_epoch_api_admission;

#[path = "native_audit_contracts.rs"]
mod suite_native_audit_contracts;

#[path = "audit_charge_boundaries.rs"]
mod suite_audit_charge_boundaries;

#[path = "audit_refinement_contracts.rs"]
mod suite_audit_refinement_contracts;

#[path = "complex_lu_finiteness.rs"]
mod suite_complex_lu_finiteness;

#[path = "audit_mos_polarity.rs"]
mod suite_audit_mos_polarity;

#[path = "circuit_admission.rs"]
mod suite_circuit_admission;

#[path = "configured_grid_contracts.rs"]
mod suite_configured_grid_contracts;

#[path = "configured_execution_contracts.rs"]
mod suite_configured_execution_contracts;

#[path = "dc_validation_contracts.rs"]
mod suite_dc_validation_contracts;

#[path = "expression_boundary_contracts.rs"]
mod suite_expression_boundary_contracts;

#[path = "experiment_boundary_contracts.rs"]
mod suite_experiment_boundary_contracts;

#[path = "lookup_response_contracts.rs"]
mod suite_lookup_response_contracts;
