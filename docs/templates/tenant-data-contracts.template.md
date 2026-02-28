# Plantillas de datos por tenant

## vehicles_inventory
- `vehicle_id`
- `model`
- `daily_rate`
- `discount_rule`
- `status` (`available|reserved|maintenance`)
- `next_available_date`
- `notes`

## reservations
- `reservation_id`
- `customer_name`
- `phone_e164`
- `vehicle_id`
- `start_date`
- `end_date`
- `quoted_total`
- `payment_status` (`pending|partial|paid|rejected`)
- `proof_url`
- `conversation_owner_agent`
- `session_id`
- `updated_at`

## collections_yyyy_mm_dd
- `customer_id`
- `customer_name`
- `phone_e164`
- `amount_due`
- `due_date`
- `message_template_key`
- `status` (`pending|sent|failed|replied`)
- `last_attempt_at`
- `attempt_count`
- `error_reason`

## Convenciones
- Teléfono en E.164
- Fecha en `YYYY-MM-DD`
- Estados por enum
- Actualización con trazabilidad (`correlation_id`)
