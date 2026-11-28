# Plan: agenda conectada y base de conocimiento

Estado: propuesta, sin código de producción. Fecha: 2026-11-28.
Objetivo: que AgentFlow pueda venderse a negocios que trabajan con citas. Empezamos por clínicas dentales y seguimos con estética, veterinarias y talleres.
El asistente tiene que:
- responder con datos reales del negocio (sucursales, horarios, servicios, precios, preguntas frecuentes, políticas);
- agendar, cancelar y reprogramar citas en el calendario del negocio, con recordatorios y paso a una persona.

**Decisión del usuario (2026-11-28):** el calendario es **de AgentFlow** y es la fuente de verdad. Google Calendar es una sincronización opcional, no la base. Las razones:
- muchas clínicas no usan Google (papel, Excel, software dental, Outlook);
- la lógica de reserva (duración, doctor/sillón, márgenes, horario de sucursal, feriados) tiene que ser nuestra de todos modos;
- la verificación de Google para scopes sensibles puede tardar semanas;
- recordatorios, reprogramación e historial deben vivir donde vive la conversación.

La agenda se construye en tres etapas:
1. **Calendario propio**, vendible por sí solo.
2. **Sincronización con Google.**
3. **Otras integraciones** (Outlook, software dental), detrás de la misma interfaz de sincronización.

Reglas que aplican a todas las fases:
- **Multitenant desde el inicio.** Toda colección lleva `TenantId` y sus índices empiezan por `TenantId`. Ninguna herramienta acepta un tenant o un cliente que venga del modelo.
- **Paridad de canales.** WhatsApp (QR, Meta y Twilio), web, API y voz se comportan igual. Las herramientas solo leen la identidad del cliente desde la metadata del canal (`channel.from`, `channel.session_id`), igual que `SalesAssistantToolBase`, así que no hay lógica por canal. Lo único distinto en voz es el formato: máximo 3 opciones y fechas dichas en palabras.
- **Un PR por fase, siempre contra `main`.** Nunca se apilan. Cada fase empieza desde `main` ya actualizado con la anterior.
- **Secretos.** Los secretos nunca se imprimen ni se escriben en logs. Los tokens se guardan cifrados con la misma protección que Twilio y Meta.
- **Tareas del usuario.** Crear cuentas, configurar la pantalla de consentimiento OAuth, verificar la app en Google y aceptar términos le toca al usuario (sección 6).

---

## 1. Auditoría: qué existe hoy

### 1.1 Datos del negocio (Commerce)

| Pieza | Dónde | Estado |
|---|---|---|
| Sucursales | `CommerceBranchDocument` (`src/AgentFlow.Api/Commerce/CommerceStore.cs:1110`), CRUD en `CommerceController` (`inventory/branches`, líneas 414-480), pestaña "Sucursales" en `CommerceAdminPage.tsx` | Guarda código, nombre, dirección, teléfono, activo y un `Properties` libre. **No guarda horario, zona horaria, días cerrados ni ubicación.** |
| Catálogo | `CommerceInventoryItemDocument` (línea 1054) | Ya distingue `ItemType` "service" de "physical" y guarda precio, descripción, categorías, `BranchIds`, stock por sucursal, variaciones y descuento. **Para un servicio agendable le falta duración, margen entre citas, "precio desde", preparación y quién puede hacerlo.** |
| Categorías | `CommerceCategoryDocument` | Existe. |
| Configuración de la tienda | `CommerceStoreSettingsDocument` | Guarda nombre comercial, moneda, idioma e impuesto. **No guarda zona horaria.** |
| Contexto del tenant en el prompt | `TenantAgentContextService` (`src/AgentFlow.Api/Settings/`), página `AgentContextSettingsPage.tsx`, inyectado por `AgentExecutionEngine` | Es texto markdown "global" que se agrega a **todos** los agentes. Hoy es la única "base de conocimiento" real: cuesta tokens en cada turno, no tiene estructura y no escala más allá de unas pocas páginas. |

### 1.2 Qué datos lee el asistente como herramienta

- **Ventas** (`SalesAssistantTools.cs`):
  - `af_sales_search_catalog` devuelve sku, nombre, descripción, tipo, precio y disponibilidad;
  - `af_sales_quote` y `af_sales_create_sale` cotizan y registran la venta;
  - `af_sales_get_customer_context` da el contexto del cliente.
- **Cobranza** (`CollectionsAssistantTools.cs`): cartera, promesas, instrucciones de pago, y `af_conversation_handoff_to_human` (línea 157), que también usa ventas.
- **Ninguna herramienta lee sucursales, horarios ni direcciones.** Hoy el asistente puede contestar "¿cuánto cuesta X?" pero no "¿dónde están?", "¿abren el sábado?" ni "¿cuánto dura una limpieza?".
- `src/AgentFlow.McpServer/Tools/CommerceTools.cs` sí toca sucursales, pero es para el asistente de configuración (admin), no para el cliente.

### 1.3 Búsqueda vectorial

- `RagRetrieverPlugin` (`src/AgentFlow.Extensions/Plugins/RagRetrieverPlugin.cs`) es un stub: devuelve `{"documents":[]}` y su health check dice "(simulated)".
- Qdrant está **deshabilitado** (`appsettings.json`: `"Qdrant": { "Enabled": false }`). Sin él se registra `NullVectorMemory`.
- Aunque se habilite, `QdrantVectorMemory` usa `DeterministicEmbeddingEncoder` (un hash de 256 dimensiones, sin semántica). El fallback de intents usa `HashEmbeddingGenerator`. No hay ningún proveedor real de embeddings conectado.
- Conclusión: **no existe búsqueda semántica real.** Activarla exige un proveedor de embeddings más un índice, y eso no hace falta para la primera versión (sección 2.4).

### 1.4 Autenticación y credenciales

- `POST /api/auth/sign-in` (`AuthController.cs`) valida **un único admin configurado** en `Auth:Admin:*` y emite un JWT con todos los permisos. `sign-up` responde 501 ("until IAM module is implemented"). No hay usuarios, roles por persona ni login con terceros.
- Credenciales de proveedores: `TenantConnectionsController` + `MongoTenantConnectionStore`, con las colecciones `tenant_connections` (config visible) y `tenant_connection_secrets` (`CipherText`).
  - El cifrado es ASP.NET DataProtection con el propósito `"tenant-connections-secrets-v1"`.
  - El secreto puede ser texto o un JSON (`TwilioCredentials.AuthTokenAsync`).
  - Hay versión, rotación, expiración, health check y auditoría de uso (`tenant_connection_usage`).
- **No existe ningún flujo OAuth** en el backend ni en el frontend.
- **Riesgo encontrado:** `DependencyInjection.cs:60` llama `AddDataProtection()` solo con `SetApplicationName`, **sin `PersistKeysTo…`**. El anillo de llaves vive en el perfil del usuario de la máquina. Si cambia la máquina, el contenedor o el content root, los secretos guardados quedan ilegibles (el handover de voz ya tuvo que fijar `DataProtection__ApplicationName` por esto). Hay que resolverlo antes de guardar refresh tokens de Google.

### 1.5 Recordatorios, conversación y handoff

- **Campañas:**
  - audiencia `commerce` o `collections` (`CampaignAudienceService.cs:70`);
  - ventana horaria y zona horaria por campaña (`CampaignFollowUps.cs:68`);
  - follow-ups con triggers `no_response`, `outcome:<code>`, `promise_due` (con `BeforeHours`) y `promise_broken`;
  - plantillas de WhatsApp aprobadas para enviar fuera de la ventana de 24 h;
  - llamadas de voz.
- `promise_due` es exactamente el patrón de un recordatorio de cita: "avisar N horas antes de una fecha".
- La conversación del cliente es una sola en todos los canales, y el handoff usa la bandeja del cliente.
- Agenda: lo único que existe es la etiqueta "Agendar demos/citas" en `AssistantBuilderController.cs:443,472`.

---

## 2. Base de conocimiento: diseño

### 2.1 Principio

Lo que es estructurado se consulta con herramientas que leen los datos reales. Así el asistente no inventa precios ni horarios, y lo que el negocio edita en la UI se refleja al instante sin reindexar.
Lo que es texto libre (FAQ, políticas) va a un almacén pequeño con búsqueda por texto en Mongo.
Los vectores solo entran si un tenant necesita cargar documentos largos.

### 2.2 Qué ya se puede contestar y qué falta

| Pregunta del paciente | Hoy | Con el plan |
|---|---|---|
| "¿Cuánto cuesta una limpieza?" | Sí (catálogo) | Sí, con "precio desde" si el precio varía |
| "¿Dónde están? ¿Tienen sucursal en X?" | No (los datos existen, pero no hay herramienta) | KB1 |
| "¿Abren el sábado? ¿A qué hora cierran?" | No (no hay horario) | KB1 |
| "¿Cuánto dura un blanqueamiento? ¿Debo ir en ayunas?" | No | KB2 |
| "¿Aceptan seguro X? ¿Cuál es la política de cancelación? ¿Duele?" | Solo si está en el markdown global | KB3 |
| Manual o protocolo largo en PDF | No | KB4 (opcional) |

### 2.3 Modelo de datos

**KB1, sucursal:** se extiende `CommerceBranchDocument` (los campos nuevos son opcionales, así que los documentos viejos se siguen leyendo):

```text
Timezone        string   (IANA, p. ej. "America/Managua"; si falta, se usa el de la tienda)
OpeningHours    List<WeeklyInterval>  { DayOfWeek, Open "08:00", Close "12:00" }  (varios intervalos por día = pausa de almuerzo)
Closures        List<DateClosure>     { Date, Reason, AllDay | intervals }        (feriados, vacaciones)
MapsUrl, Latitude, Longitude, Notes (texto corto: parqueo, referencias)
```

**KB1, tienda:** `CommerceStoreSettingsDocument` gana `Timezone` (por defecto "America/Managua") y `BusinessDescription` (texto corto: qué hace el negocio).

**KB2, servicio agendable:** se extiende `CommerceInventoryItemDocument` (solo aplica si `ItemType == "service"`):

```text
Bookable          bool
DurationMinutes   int
BufferMinutes     int      (limpieza o preparación entre citas)
PriceIsFrom       bool     ("desde C$ 800")
Preparation       string?  (instrucciones previas: ayuno, traer estudios)
ResourceKinds     List<string>  (p. ej. ["dentist","chair"]: qué recursos ocupa; se usa en CAL1)
```

**KB3, almacén de conocimiento:** colección nueva `kb_entries`:

```text
Id, TenantId, Kind ("faq" | "policy" | "info"), Title, Question?, Answer (markdown corto),
Tags[], BranchIds[], ServiceSkus[], Active, Priority, UpdatedAt, UpdatedBy
Índices: { TenantId, Active }, text index sobre Title/Question/Answer/Tags (default_language "spanish", tenant filtrado en la query)
```

### 2.4 ¿Hace falta búsqueda vectorial?

No para empezar.
- Una clínica tiene del orden de 20 a 200 FAQs y políticas.
- El `$text` de Mongo 7 con stemming en español, más el filtro por tags y servicio, alcanza. El modelo también reformula la consulta: la herramienta acepta `query` y, si no hay resultados, reintenta por palabras (el mismo patrón que `SalesSearchCatalogTool`).
- Si `$text` no da resultados, la herramienta devuelve las entradas con mayor `Priority` (máximo 5) para que el modelo decida.

Los vectores (KB4) se justifican cuando un tenant carga documentos largos o hay más de unas 500 entradas. En ese caso hay que:
- conectar un proveedor real de embeddings (p. ej. `text-embedding-3-small` con el perfil de modelo del tenant);
- reemplazar `DeterministicEmbeddingEncoder` y `HashEmbeddingGenerator`;
- elegir el índice: Qdrant, que ya tiene cliente, o una colección de Mongo con similitud calculada en memoria si son pocos miles de chunks;
- reemplazar o borrar el stub `RagRetrieverPlugin`.

### 2.5 Herramientas del asistente (KB)

| Herramienta | Entrada | Salida |
|---|---|---|
| `af_business_get_info` | `branch?` (nombre o código), `date?` | Nombre y descripción del negocio, sucursales con dirección, teléfono, mapa, horario de la semana, si está abierto ahora y el próximo cierre, en la zona horaria de la sucursal |
| `af_sales_search_catalog` (se extiende) | igual | Agrega `durationMinutes`, `bookable`, `priceIsFrom` y `preparation` |
| `af_kb_search` | `query`, `service?`, `branch?` | Hasta 5 entradas `{id, title, answer}`. El prompt exige responder solo con esto y, si no hay respuesta, decir que no lo sabe y ofrecer un asesor |

Las tres son de solo lectura, de riesgo bajo, y se registran en `DependencyInjection.cs` como las de ventas.

---

## 3. Agenda: diseño (calendario propio + sincronización opcional)

### 3.1 Decisiones clave

1. **Calendario propio como fuente de verdad (decisión aprobada).** La cita vive en AgentFlow, en la colección `appointments`. Un proveedor externo solo puede hacer dos cosas, ambas detrás de la interfaz `ICalendarSync`:
   - recibir una copia de la cita (escribir el evento);
   - aportar bloques de "ocupado" (free/busy).

   Así:
   - los tenants sin Google tienen la agenda completa;
   - las herramientas del asistente son las mismas con o sin sincronización;
   - Outlook y el software dental se suman en la etapa 3 con la misma interfaz.
2. **"Conectar Google Calendar" no es "iniciar sesión con Google".** Hoy no hay usuarios (un admin fijo en config), así que el login con Google depende de un módulo IAM que no existe. Mezclar las dos cosas bloquearía la agenda detrás de IAM.
   - El plan hace **OAuth de autorización** (código + refresh token) como una *conexión del tenant*, igual que Twilio o Meta. Su botón es "Conectar Google Calendar" en Canales/Conexiones.
   - "Iniciar sesión con Google" queda para el proyecto IAM. Cuando exista, ese mismo cliente OAuth puede pedir el permiso de calendario después, con consentimiento incremental (`include_granted_scopes=true`).
3. **Conexión por tenant o por recurso, ambas en la etapa 2.**
   - Una conexión es una cuenta de Google con su refresh token cifrado. Puede ser del tenant o de un recurso (`Config.resourceId`). Cada recurso (doctor, sillón) se mapea a un `calendarId` dentro de alguna conexión.
   - Caso A (CAL5): la cuenta de la clínica ve los calendarios de los doctores, que se los compartieron. Basta una conexión.
   - Caso B (también CAL5): cada doctor conecta su propia cuenta con un enlace de un solo uso que el admin le envía. La conexión queda ligada a su recurso. No requiere IAM.
4. **Permisos mínimos.** Ver 3.6.

### 3.2 Modelo de datos

```text
calendar_resources
  Id, TenantId, Kind ("staff" | "room" | "equipment"), Name, BranchId,
  ServiceSkus[] (qué servicios atiende; vacío = todos los del ResourceKind),
  ResourceKind ("dentist", "chair"...), WorkingHours[] (opcional: si falta, usa el horario de la sucursal),
  Timezone (heredada de la sucursal), Active,
  Binding { Provider: "internal" | "google", ConnectionId?, CalendarId?, BusyCalendarIds[] }

appointments
  Id, TenantId, Reference (8 caracteres, como las ventas), PartyId, CustomerChannel, CustomerIdentifier,
  PatientName? (si la cita es para otra persona: hijo, mascota), ServiceSku, ServiceName,
  BranchId, ResourceIds[], StartUtc, EndUtc, Timezone,
  Status ("confirmed" | "cancelled" | "completed" | "no_show"), CancelReason?,
  Source ("assistant" | "staff" | "google"), SessionId?, ThreadId?, CreatedBy,
  External { Provider, ConnectionId, CalendarId, EventId, ETag, SyncedAt, SyncError? }[],
  ReminderState { SentKeys[] }, IdempotencyKey, CreatedAt, UpdatedAt
  (ServiceSku apunta a un ítem de servicio del catálogo de Commerce y BranchId a CommerceBranchDocument:
   no se duplican servicios ni sucursales; la duración y el margen salen del catálogo (KB2) y el horario de la sucursal (KB1))
  Índices: {TenantId, StartUtc}, {TenantId, PartyId, StartUtc}, {TenantId, ResourceIds, StartUtc}, único {TenantId, IdempotencyKey}

appointment_slot_claims          (evita la doble reserva sin transacciones)
  { TenantId, ResourceId, SlotKey ("2026-12-03T14:15Z"), AppointmentId }   índice ÚNICO {TenantId, ResourceId, SlotKey}

calendar_settings (una por tenant)
  SlotGranularityMinutes (15), MinLeadMinutes (120), MaxHorizonDays (45),
  CancelMinHours (24: con menos anticipación cancela o reprograma una persona),
  ReminderOffsetsHours ([24, 2]), EventTitleTemplate ("Cita: {servicio} - {iniciales}"), ShareCustomerPhoneWithGoogle (false)
```

**Anti doble reserva.** Al reservar se insertan con `InsertMany(ordered: true)` las claims de cada unidad de 15 minutos que ocupa la cita (duración + buffer) por cada recurso.
- Si alguna ya existe, el índice único lo rechaza: se borran las claims insertadas y se responde "ese horario se acaba de ocupar", con alternativas.
- Al cancelar o reprogramar se liberan las claims.
- No depende de que Mongo corra como replica set (las transacciones sí lo exigirían).

### 3.3 Motor de disponibilidad (`AvailabilityService`)

```text
slots(servicio, sucursal?, recurso?, desde, hasta) =
  para cada recurso apto (ServiceSkus/ResourceKind, sucursal, activo):
    ventanas = horario del recurso (o de la sucursal) − Closures, en la zona horaria local
    ocupado  = citas confirmadas (de AgentFlow) ∪ free/busy de Google de BusyCalendarIds (si hay conexión)
    candidatos = cada SlotGranularity dentro de ventanas donde [inicio, inicio + duración + buffer) no choca con ocupado
    filtrar: inicio ≥ ahora + MinLeadMinutes, inicio ≤ ahora + MaxHorizonDays
  ordenar por cercanía y repartir entre recursos; devolver los N primeros
```

- Todo se calcula en UTC y se presenta en la zona horaria de la sucursal (con pruebas de DST, aunque Nicaragua no lo tenga).
- El free/busy de Google se cachea 60 s en Redis por `(conexión, calendario, rango)`.

### 3.4 Herramientas del asistente (agenda)

En todas, el cliente sale de la metadata del canal, nunca del modelo. El cliente solo ve y toca **sus** citas.

| Herramienta | Entrada | Comportamiento |
|---|---|---|
| `af_calendar_find_slots` | `service` (sku o texto), `branch?`, `resource?` ("con la doctora Pérez"), `date?` (ISO), `partOfDay?` (mañana/tarde), `limit?` | Devuelve `today` local, el servicio resuelto (duración, precio) y hasta 6 slots `{slotId, start local, resource, branch}`. `slotId` es un token opaco firmado (recurso + inicio + sku) para que el modelo no invente horarios. Si no hay nada en el rango, sugiere el primer día con espacio |
| `af_calendar_book` | `slotId`, `patientName?`, `notes?` | Revalida (incluido el free/busy de Google en vivo), crea la cita con claims, la escribe en Google si aplica, enlaza la party y la sesión. Es idempotente por `(sesión, slotId)` |
| `af_calendar_my_appointments` | (nada) | Citas futuras del cliente, con referencia |
| `af_calendar_cancel` | `appointmentId` o `reference`, `reason?` | Respeta `CancelMinHours`. Fuera de plazo devuelve `needsHuman` y el asistente hace handoff |
| `af_calendar_reschedule` | `appointmentId`, `slotId` | Reserva el nuevo horario y libera el anterior, sin perder la cita si el nuevo falla |

Además:
- Un nuevo `AppointmentReferenceReplyTransformer`, como `SaleReferenceReplyTransformer`, para que la referencia nunca la escriba el modelo.
- Un nuevo **"Asistente de citas"** (`AppointmentsAssistantAgent`: prompt + lista de herramientas, sembrado en `SeedData.cs` como el de ventas) con `af_business_get_info`, `af_sales_search_catalog`, `af_kb_search`, las 5 de agenda y `af_conversation_handoff_to_human`. La plantilla "Agendar demos/citas" del builder pasa a usarlo.
- Reglas del prompt:
  - una pregunta por turno;
  - máximo 3 opciones (en voz, siempre);
  - confirmación explícita antes de reservar;
  - urgencias (dolor fuerte, sangrado, trauma) van directo a un humano;
  - nunca dar diagnósticos.

### 3.5 Recordatorios, confirmaciones y handoff

Se reutilizan campañas, sin crear otro motor:
- Una nueva fuente de audiencia `appointments` en `CampaignAudienceService`: citas `confirmed` que empiezan dentro de la ventana, con variables `{servicio}`, `{fecha}`, `{hora}`, `{sucursal}` y `{referencia}`.
- Un nuevo trigger de follow-up `appointment_due` (con `BeforeHours`, análogo a `promise_due`).
- Una campaña de sistema por tenant, "Recordatorios de citas", creada al activar el módulo, con reglas de 24 h y de 2 h. Fuera de la ventana de 24 h de WhatsApp usa una plantilla aprobada (la regla ya existe). Opcional: recordatorio por llamada de voz con las campañas de llamada.
- La respuesta del cliente ("confirmo", "no puedo", "cámbiala") cae en la misma conversación. El asistente de citas tiene el contexto de la cita (`af_calendar_my_appointments`) y la marca o la reprograma.
- Nuevos outcomes de campaña: `appointment_confirmed`, `appointment_cancelled` y `appointment_rescheduled`; más adelante, `no_show` desde la UI.
- Handoff con `af_conversation_handoff_to_human` ya existente: no hay horario en el horizonte, cancelación fuera de plazo, urgencia, el cliente lo pide o una herramienta falla. El personal ve y gestiona la cita desde la UI (CAL2).

### 3.6 Google: OAuth, permisos y sincronización

**Iniciar sesión con Google y consentimiento incremental.** Hoy el login es un admin fijo, así que "iniciar sesión con Google" no puede reemplazarlo hasta que exista IAM. El cliente OAuth se diseña para servir a ambos casos:
- **Ahora:** el botón "Conectar Google" pide `openid email` más los scopes de calendario.
- **Cuando exista IAM:** el login pide solo `openid email profile`, y la primera vez que alguien conecte la agenda se le pide el permiso de calendario con `include_granted_scopes=true`.

El callback, el `state` y el almacenamiento del token son los mismos en los dos casos.

**Flujo (CAL5)**
1. El admin hace clic en "Conectar Google Calendar" → `POST /api/v1/tenants/{t}/integrations/google-calendar/connect` (requiere JWT y permiso `TenantManage`).
2. El backend genera `state` (aleatorio, 10 min de vida, guardado en Redis con tenant, usuario y `code_verifier` PKCE) y devuelve la URL de Google con estos parámetros: `access_type=offline`, `prompt=consent` (garantiza el refresh token), `include_granted_scopes=true`, PKCE S256 y `redirect_uri` fija.
3. Google redirige a `GET /api/integrations/google-calendar/callback`. Es la **única** URI de redirección para todos los tenants, y el tenant **no** viene en la URL: se recupera del `state`.
4. El backend valida `state` (un solo uso), intercambia el código y obtiene el email de la cuenta (`openid email`). Guarda una `TenantConnection` `{ConnectorId: "google-calendar", Type: Rest, Config: {accountEmail, grantedScopes, connectedAt}}` y el secreto `{"refreshToken": "..."}` cifrado con `"tenant-connections-secrets-v1"`. Después redirige al frontend con `?connected=google`.
5. El access token se cachea en memoria o Redis hasta que vence. Nunca se persiste en claro ni se loguea.
6. Si Google responde `invalid_grant` (token revocado o expirado), la conexión pasa a `Unhealthy`, se avisa en la UI y en la bandeja, y la agenda sigue funcionando en modo interno.
7. Desconectar: revocar en `oauth2.googleapis.com/revoke` y borrar el secreto.

**Permisos (de menor a mayor alcance)**

| Opción | Scopes | Para qué |
|---|---|---|
| **Recomendada** | `calendar.app.created` + `calendar.freebusy` + `calendar.calendarlist.readonly` + `openid email` | AgentFlow crea un calendario secundario por recurso ("Citas AgentFlow – Dra. Pérez") y solo escribe ahí. Lee únicamente ocupado/libre de los calendarios personales elegidos. El doctor se suscribe al calendario creado y ve sus citas en su teléfono |
| Escribir en calendarios propios | `calendar.events.owned` + `calendar.freebusy` + `calendar.calendarlist.readonly` | La cita aparece en el calendario principal de la cuenta conectada |
| Escribir en calendarios compartidos (caso A con escritura directa) | `calendar.events` | Más amplio; solo si un cliente lo exige |

- El permiso extra se pide con consentimiento incremental.
- Que los scopes de Calendar sean "sensibles" hay que confirmarlo en la pantalla de consentimiento de Google Cloud Console, que marca cada scope. Si lo son, Google exige verificar la app antes de publicarla para usuarios externos: dominio verificado, política de privacidad, justificación de cada scope y un video del flujo. No deberían ser "restringidos", así que no se espera la auditoría de seguridad CASA.
- Mientras la app esté en modo **Testing**:
  - solo pueden conectarse hasta 100 usuarios de prueba agregados a mano;
  - **los refresh tokens vencen a los 7 días**.
  Sirve para la demo, no para un cliente real.

**Sincronización (CAL6)**
- **Escritura:** `events.insert`, `patch` o `delete` en el `CalendarId` del recurso, con `extendedProperties.private = {agentflowTenantId, agentflowAppointmentId}`, título según `EventTitleTemplate` y un mínimo de datos personales (sin teléfono ni notas clínicas, salvo que el tenant lo active). Si Google falla, la cita **igual queda confirmada** en AgentFlow con `SyncError`, y un worker reintenta.
- **Lectura de ocupado:** `freebusy.query` sobre `BusyCalendarIds` al buscar slots (con caché) y otra vez justo antes de reservar.
- **Cambios hechos en Google por el personal:** un worker cada 2-5 min hace `events.list` con `syncToken` sobre los calendarios escritos por AgentFlow.
  - Un evento borrado o movido allí marca la cita (`cancelled` o con la nueva hora) y crea un aviso en la bandeja para que una persona avise al cliente. No se notifica al cliente de forma automática en la primera versión.
  - Las push notifications (`events.watch`) quedan para después: requieren HTTPS público estable y renovar el canal.

### 3.7 Agenda sin Google

No es un fallback: es el producto de la etapa 1. Un recurso sin sincronización tiene `Binding.Provider = "internal"`. El personal ve y edita la agenda en la UI (CAL2), y el asistente reserva igual que con Google.

### 3.8 Interfaz de sincronización (`ICalendarSync`)

```csharp
interface ICalendarSync {
  string Provider { get; }                                   // "google", "outlook", "dentalink"...
  Task<IReadOnlyList<BusyBlock>> GetBusyAsync(binding, fromUtc, toUtc, ct);
  Task<ExternalRef> UpsertEventAsync(binding, appointment, ct);   // crear o mover
  Task DeleteEventAsync(binding, ExternalRef, ct);
  Task<SyncChanges> PullChangesAsync(binding, cursor, ct);       // cambios hechos fuera de AgentFlow
}
```

- `AvailabilityService` suma los `BusyBlock` de todos los bindings del recurso.
- `AppointmentService` llama `Upsert` o `Delete` después de confirmar en AgentFlow. Una falla de sincronización nunca deshace la cita.
- Un worker genérico reintenta y ejecuta `PullChangesAsync`.
- La etapa 1 trae solo `InternalCalendarSync` (no-op); cada etapa siguiente agrega una implementación.

---

## 4. Fases (un PR cada una, contra `main`, en este orden)

| Bloque | Fases (en orden) | Resultado |
|---|---|---|
| Base de conocimiento | KB1 → KB2 → KB3 (KB4 opcional) | El asistente responde con datos reales del negocio. KB1 y KB2 también son la base de la agenda (horario de sucursal, duración del servicio) |
| **Etapa 1: Calendario propio** | CAL1 → CAL3 → CAL2 → CAL4 | **Vendible por sí sola:** recursos, horarios y feriados, citas en Mongo, reserva, cancelación y reprogramación por todos los canales, recordatorios, handoff y UI de agenda |
| **Etapa 2: Sincronización con Google** | SEC0 → CAL5 → CAL6 | Conectar Google (por clínica o por doctor), enviar las citas al calendario del doctor y bloquear lo que esté ocupado en Google |
| **Etapa 3: Otras integraciones** | CAL7 (Outlook), CAL8 (software dental), CAL9 (feed ICS) | Más destinos detrás de `ICalendarSync`, según los pida cada cliente |

Orden recomendado de PRs: KB1, KB2, CAL1, CAL3, CAL2, CAL4, KB3, SEC0, CAL5, CAL6.
- KB3 puede ir antes o en paralelo con la etapa 1, porque no comparte archivos con ella.
- El trámite de Google (sección 6) lo puede empezar el usuario ya, porque la verificación puede tardar semanas y no bloquea la etapa 1.

### KB1: Sucursales con horario y herramienta de información del negocio
- **Backend:**
  - `CommerceStore.cs`: campos de 2.3 en `CommerceBranchDocument` y `CommerceStoreSettingsDocument`; un helper `BusinessHours` (abierto ahora, próximo cierre, intervalos del día) en `src/AgentFlow.Api/Commerce/BusinessHours.cs`.
  - `CommerceController.cs`: aceptar y devolver los campos nuevos en los endpoints de sucursales y settings, con validación (formato HH:mm, intervalos sin solaparse, zona IANA válida).
  - Herramienta `af_business_get_info` en `src/AgentFlow.Api/Knowledge/BusinessInfoTool.cs`, registrada en `DependencyInjection.cs`.
  - Agregar la herramienta a los asistentes de ventas y cobranza (`SeedData.cs` y la lista `Tools`), con una línea en sus prompts.
- **UI:** en `CommerceAdminPage.tsx`, editor de horario semanal en el diálogo de sucursal (con copiar lunes a viernes), días cerrados, zona horaria y mapa; zona horaria y descripción en Configuración.
- **Pruebas** (`tests/AgentFlow.Tests.Unit/Knowledge/BusinessHoursTests.cs`): pausa de almuerzo, feriado, cruce de medianoche, zona horaria; prueba de la herramienta con un store fake.
- **Usuario:** nada.

### KB2: Servicios agendables en el catálogo
- **Backend:**
  - campos de 2.3 en `CommerceInventoryItemDocument`, más su validación en el upsert;
  - `SalesSearchCatalogTool` devuelve `durationMinutes`, `bookable`, `priceIsFrom` y `preparation`;
  - el prompt de ventas dice "precio desde" cuando corresponde.
- **UI:** sección "Agenda" en el diálogo de producto cuando el tipo es servicio.
- **Pruebas:** serialización con documentos viejos sin los campos; salida de la herramienta.
- **Usuario:** nada.

### KB3: Preguntas frecuentes y políticas
- **Backend:**
  - `src/AgentFlow.Api/Knowledge/KnowledgeStore.cs` (colección `kb_entries` e índice de texto creado al arrancar);
  - `KnowledgeController.cs` en `api/v1/tenants/{t}/knowledge/entries` (CRUD, importación CSV `question,answer,tags`, y `POST /search` para probar);
  - herramienta `af_kb_search`;
  - módulo nuevo `AppModules.Knowledge` ("knowledge", "Base de conocimiento", DefaultEnabled: true).
- **UI:**
  - página nueva `pages/knowledge/KnowledgePage.tsx`: lista, editor, importar y una caja "probar pregunta" que muestra qué entradas encontraría el asistente;
  - ruta en `routes/sections/dashboard.tsx`, entrada en `layouts/nav-config-dashboard.tsx` y endpoints en `lib/axios.ts`.
- **Prompts:** ventas, cobranza y (luego) citas reciben la regla "para dudas del negocio usa `af_kb_search`; si no está, dilo y ofrece un asesor". El markdown global queda para reglas de tono, no para datos.
- **Pruebas:** aislamiento entre tenants (la búsqueda del tenant A nunca devuelve entradas de B), búsqueda en español con acentos, fallback por palabras. Hay una prueba de integración en `tests/AgentFlow.Tests.Integration/Knowledge/` con Mongo.
- **Usuario:** cargar las FAQ reales de la clínica piloto.

---

## Etapa 1: Calendario propio

Depende de KB1 (horario de sucursal) y de KB2 (servicios con duración). Al terminar CAL4 el producto se puede vender sin ninguna integración externa.

### CAL1: Núcleo de agenda propia (sin UI de calendario, sin asistente)
- **Backend:** carpeta nueva `src/AgentFlow.Api/Scheduling/`:
  - `SchedulingStore.cs` (las colecciones de 3.2, sus índices y las claims);
  - `AvailabilityService.cs` (3.3);
  - `AppointmentService.cs` (book, cancel, reschedule y complete, con claims e idempotencia);
  - `ICalendarSync.cs` (interfaz de 3.8, con `InternalCalendarSync` no-op);
  - `SchedulingController.cs` en `api/v1/tenants/{t}/scheduling`:
    - `resources` (CRUD);
    - `GET availability?service&branch&resource&from&to`;
    - `appointments` (`GET` por rango, recurso o cliente; `POST`; `POST {id}/cancel`, `{id}/reschedule`, `{id}/status`);
    - `settings`.
  - Módulo `AppModules.Scheduling` ("scheduling", "Agenda", DefaultEnabled: false) con `[RequiresAppModule]`.
- **Pruebas** (`tests/AgentFlow.Tests.Unit/Scheduling/`):
  - disponibilidad: horario, buffer, lead time, horizonte, cierres, varios recursos, zona horaria;
  - **concurrencia**: dos reservas simultáneas del mismo slot, exactamente una gana (integración con Mongo real por el índice único);
  - reprogramar sin perder la cita;
  - aislamiento entre tenants.
- **Usuario:** nada.

### CAL3: Asistente de citas (herramientas y prompt)
- **Backend:**
  - `src/AgentFlow.Api/Scheduling/AppointmentAssistantTools.cs`, con las 5 herramientas de 3.4 y una base como `SalesAssistantToolBase`, aunque convendría extraer una base común `CustomerToolBase` en `src/AgentFlow.Api/Assistants/`;
  - firma y verificación de `slotId` con DataProtection (propósito `scheduling-slot-v1`, 30 min de vida);
  - `AppointmentReferenceReplyTransformer`;
  - `AppointmentsAssistantAgent` (prompt y herramientas) en `SeedData.cs`;
  - la plantilla "Agendar demos/citas" de `AssistantBuilderController.cs` apunta a él.
- **Paridad:** las herramientas no conocen el canal. Una prueba ejecuta el mismo guion (buscar → reservar → listar → cancelar) con metadata de cada canal (QR, Meta, Twilio, web, API y voz) y exige el mismo resultado.
- **Pruebas:**
  - el cliente B no puede cancelar la cita de A aunque el modelo pase su id;
  - un `slotId` inventado o vencido se rechaza;
  - cancelación fuera de plazo devuelve `needsHuman`;
  - idempotencia ante el doble "sí".
- **Validación manual:** conversación real por WhatsApp (canal de pruebas) y por voz con el asistente de citas.
- **Usuario:** decidir qué canal o número usa el asistente de citas en la demo.

### CAL2: UI de agenda
- **Frontend:** página nueva `pages/scheduling/`:
  - vista semanal y diaria por recurso o sucursal;
  - crear una cita manual (buscar cliente en el CRM o crear lead);
  - mover o cancelar;
  - marcar completada o no asistió;
  - pestaña Recursos (alta de doctores y sillones, servicios que atienden y horario propio);
  - pestaña Ajustes (granularidad, anticipación, plazo de cancelación).
  - Ruta, nav y endpoints como en KB3. Librería de calendario: evaluar primero lo que ya traiga el template MUI del proyecto; si no hay, `@fullcalendar/react` (MIT).
- En la ficha del cliente (CRM) y en el panel de contexto de la bandeja: "Próximas citas".
- **Pruebas:** las pruebas de componentes que ya use el frontend, más un recorrido manual en el navegador.
- **Usuario:** revisar la UI con alguien de la clínica piloto.

### CAL4: Recordatorios y confirmaciones
- **Backend:**
  - fuente de audiencia `appointments` en `CampaignAudienceService.cs`;
  - trigger `appointment_due` en `CampaignFollowUps.cs` y `CampaignFollowUpService.cs`;
  - variables de plantilla en `CampaignMessageRenderer.cs`;
  - outcomes nuevos en `CampaignOutcomeService.cs`;
  - creación de la campaña de sistema "Recordatorios de citas" al habilitar `scheduling`;
  - `ReminderState` evita enviar dos veces.
- **Pruebas:** la ventana de recordatorio incluye y excluye bien las citas (zona horaria); una cita cancelada no recibe recordatorio; plantilla fuera de 24 h.
- **Usuario:** crear y aprobar en Meta la plantilla de WhatsApp de recordatorio (texto propuesto en el PR).

---

## Etapa 2: Sincronización con Google

Opcional por tenant. Si no se conecta nada, la agenda sigue igual que en la etapa 1.

### SEC0: Llaves de DataProtection persistentes (prerrequisito de Google)
- **Backend:** `DependencyInjection.cs`:
  - `PersistKeysToFileSystem` sobre un volumen configurable (`DataProtection:KeysPath`), o en Mongo con un `IXmlRepository` propio (colección `dataprotection_keys`), más `ProtectKeysWith…` si hay certificado;
  - migración documentada: arrancar una vez con el anillo actual para copiarlo, sin re-cifrar secretos.
- **Pruebas:** un secreto cifrado se lee tras reiniciar con otra instancia que comparte el repositorio de llaves.
- **Usuario:** definir dónde viven las llaves en producción.

### CAL5: Conectar Google Calendar (OAuth)
- **Backend:**
  - `src/AgentFlow.Api/Integrations/Google/`:
    - `GoogleOAuthOptions` (ClientId; ClientSecret desde secretos de configuración, nunca en el repo; RedirectUri);
    - `GoogleOAuthController` (`connect`, `callback`, `disconnect`, `status`), más `POST resources/{id}/connect-link`, que genera un enlace de un solo uso (token firmado, 48 h) para que un doctor conecte su propia cuenta; esa conexión queda con `Config.resourceId`;
    - `GoogleTokenService` (refresh, caché, `invalid_grant` → Unhealthy);
    - `GoogleCalendarClient` (HttpClient tipado, sin SDK pesado, o `Google.Apis.Calendar.v3` si se prefiere).
  - `state` y PKCE en Redis.
  - La conexión se guarda en `tenant_connections` y el secreto en `tenant_connection_secrets`.
  - El health check de `TenantConnectionsController` reconoce `google-calendar`.
- **UI:**
  - tarjeta "Google Calendar" en Canales/Conexiones con conectar, cuenta conectada y desconectar;
  - en Recursos: elegir o crear el calendario de cada recurso (lista de `calendarList`) y los calendarios que cuentan como ocupado.
- **Pruebas:**
  - `state` inválido, reusado o vencido se rechaza;
  - el tenant sale del `state`, no de la URL;
  - el intercambio y el refresh se prueban con un `HttpMessageHandler` falso;
  - el refresh token nunca aparece en logs (prueba sobre el logger);
  - desconectar revoca.
- **Usuario:** sección 6.

### CAL6: Sincronización con Google
- **Backend:**
  - `GoogleCalendarSync : ICalendarSync`, que en `AvailabilityService` aporta el free/busy (caché Redis 60 s) y en `AppointmentService` escribe, mueve y borra eventos;
  - `CalendarSyncWorker` genérico (reintentos de `SyncError` y `PullChangesAsync`; en Google, `events.list` con `syncToken`), con aviso en la bandeja si un evento cambió fuera de AgentFlow.
- **Pruebas:**
  - Google caído: la cita se confirma igual y queda pendiente de sincronizar;
  - un evento borrado en Google marca la cita y crea el aviso;
  - un `syncToken` inválido (410) obliga a resincronizar completo;
  - otro evento ocupando el horario en Google lo excluye de la disponibilidad.
- **Validación manual:** con la cuenta de prueba del usuario, reservar por WhatsApp y ver el evento en Google; bloquear una hora en Google y comprobar que el asistente ya no la ofrece.
- **Usuario:** una cuenta de Google de prueba con 2 calendarios.

---

## Etapa 3: Otras integraciones (según demanda)

Cada una es una implementación de `ICalendarSync` más su pantalla de conexión. No cambian las herramientas del asistente ni el modelo de citas.

### CAL7: Outlook / Microsoft 365
`OutlookCalendarSync` con Microsoft Graph (`Calendars.ReadWrite`, `offline_access`; free/busy con `getSchedule`; cambios con delta query). El usuario registra la app en Microsoft Entra.

### CAL8: Software dental
- Conector por producto (p. ej. el que use el primer cliente).
- Si el software tiene API, se usa para empujar citas y leer ocupado.
- Si no la tiene, la alternativa es importar y exportar CSV desde la UI.
- Antes de escribir código hay que evaluar cada software (API, costo y términos), y eso lo decide el usuario.

### CAL9: Feed ICS de solo lectura
URL secreta por recurso (`/ics/{token}.ics`) para suscribirse desde cualquier calendario (Apple, Outlook, Google) sin OAuth. Es la integración más barata para quien solo quiere ver sus citas en el teléfono.

---

## Base de conocimiento, fase opcional

### KB4 (opcional): Documentos largos con búsqueda semántica
Solo si un cliente lo pide.
- **Backend:**
  - subir PDF/DOCX y extraer el texto;
  - partirlo en chunks de unos 800 tokens con metadatos;
  - embeddings reales (proveedor configurable por tenant);
  - índice en Qdrant (`Enabled: true`, `VectorSize` real) o en Mongo;
  - `af_kb_search` combina el `$text` de las entradas con los vectores de los chunks.
- Además: reemplazar el stub `RagRetrieverPlugin` y los encoders hash, o borrarlos.
---

## 5. Riesgos y decisiones abiertas

- **Fuente de verdad.** El plan dice AgentFlow. Si una clínica quiere que Google mande (el personal agenda directo en Google), CAL6 lo cubre a medias: lo que se crea en Google bloquea disponibilidad, pero no se convierte en cita de AgentFlow. Convertirlo es una extensión posible.
- **Datos de salud.** Una clínica maneja datos sensibles. Por eso: título de evento mínimo, nada clínico en Google, notas de la cita solo en AgentFlow, y el asistente no da diagnósticos. Conviene revisar la ley de protección de datos aplicable (p. ej. Ley 787 de Nicaragua) antes de vender.
- **Hora actual para el modelo.** "Mañana a las 3" depende de que el modelo sepa la fecha local. `af_calendar_find_slots` siempre devuelve `today` en la zona de la sucursal y acepta fechas ISO. Hay que verificar en CAL3 si el engine ya inyecta la fecha.
- **Modelo del asistente.** El handover de ventas observó que gpt-4o-mini se confunde en hilos con mucha historia. Para citas conviene probar un modelo más fuerte.
- **Voz.** Leer opciones de horario por voz es más lento; el prompt limita a 3 y propone "la más cercana" primero.

## 6. Lo que debe configurar el usuario (no lo hace Claude)

1. **Google Cloud** (se puede empezar ya):
   - crear o elegir un proyecto y habilitar Google Calendar API;
   - en la pantalla de consentimiento OAuth: tipo External, nombre de la app, logo, email de soporte, dominio autorizado, enlaces a la política de privacidad y a los términos, y los scopes de 3.6 (revisar ahí cuáles marca como sensibles);
   - crear un cliente OAuth "Web application" con la redirect URI `https://<dominio-api>/api/integrations/google-calendar/callback` (y la de desarrollo, p. ej. el túnel o `http://localhost:5000/...`);
   - guardar ClientId y ClientSecret como secretos de configuración del API (variables de entorno o user-secrets), nunca en el repo ni en un chat;
   - agregar usuarios de prueba mientras la app esté en Testing (los tokens duran 7 días);
   - para clientes reales: enviar la app a verificación (dominio verificado en Search Console, política de privacidad pública que explique el uso de Calendar, justificación por scope y video del flujo de consentimiento).
2. **Meta:** aprobar la plantilla de WhatsApp de recordatorio de cita (CAL4).
3. **Producción:** dónde se guardan las llaves de DataProtection (SEC0).
4. **Piloto:** datos reales de la clínica (sucursales, horarios, servicios con duración, FAQs y políticas, doctores y sillones).

## 7. Qué queda fuera de este plan

- Login con Google y usuarios por persona (módulo IAM).
- Pagos o depósitos al reservar.
- Listas de espera y citas recurrentes (tratamientos en varias sesiones). Se pueden agregar sobre `appointments` después.
