# UX Blueprint: Flujos y Subflujos para Usuarios No Técnicos

## Objetivo
Reducir fricción para crear flujos y subflujos de agentes cuando el usuario no domina conceptos técnicos (mapeos, schemas, MCP, tools).

La estrategia es cambiar de **"configuración técnica"** a **"configuración por objetivo de negocio"** con acompañamiento guiado.

---

## 1) Principios UX (aplicados al producto actual)

1. Ocultar complejidad por defecto.
2. Guiar por objetivo, no por componente técnico.
3. Convertir errores en acciones de reparación 1-click.
4. Mostrar impacto en lenguaje de negocio.
5. Permitir modo avanzado sin contaminar el flujo básico.

---

## 2) Mapa de navegación propuesto (sobre estructura actual)

### Nuevo entrypoint principal
- **"Crear automatización"** (botón global destacado)

### Secciones actuales reutilizadas
- `Channels` -> "Canales"
- `Workflows` -> "Automatizaciones"
- `Intents` -> "Intenciones"
- `Threads` -> "Bandeja"
- `CommerceAdmin` -> "Ventas y cobros"

### Nuevo orden recomendado en menú
1. Inicio
2. Crear automatización (nuevo)
3. Canales
4. Automatizaciones (Workflows)
5. Intenciones
6. Bandeja
7. Ventas y cobros
8. Configuración asistida (Config Assistant)

---

## 3) Flujo principal: "Crear automatización" (Wizard 5 pasos)

## Paso 1: Qué quieres lograr
- Prompt libre: "Quiero vender por WhatsApp y cobrar con factura".
- Chips rápidos: `Vender`, `Cobrar`, `Soporte`, `Agendar`, `Seguimiento`.
- Salida: intención de negocio principal + objetivo secundario.

## Paso 2: Canal y entrada
- Selección de canal existente o creación rápida.
- Si canal no está sano: CTA "Resolver ahora" (activar/health).
- Salida: canal operativo validado.

## Paso 3: Qué datos necesitas del cliente
- Constructor simple tipo formulario (preguntas humanas):
  - "Nombre"
  - "Teléfono"
  - "Producto"
  - "Cantidad"
- Sin exponer JSON/schema en modo básico.
- Salida: contrato de datos implícito.

## Paso 4: Qué debe hacer el sistema al final
- Acciones de negocio predefinidas:
  - Crear venta
  - Generar factura
  - Escalar a humano
  - Enviar confirmación
- Salida: workflow draft + subflujo de agent brain.

## Paso 5: Simular y publicar
- Simulación chat embebida "Actúa como cliente".
- Explicación simple:
  - "Entendí intención X"
  - "Entré al subflujo Y"
  - "Ejecuté acción Z"
- Publicación escalonada:
  - Borrador -> Piloto 10% -> Producción.

---

## 4) Diseño de subflujos (UX no técnica)

No mostrar "workflow steps" inicialmente. Mostrar bloques semánticos:

1. **Entender necesidad** (Router)
2. **Pedir datos faltantes** (Brain)
3. **Validar datos** (Brain)
4. **Ejecutar acción** (Tool/MCP)
5. **Confirmar al cliente** (Response)

Cada bloque tiene:
- Estado: `Activo` / `Opcional`
- Texto editable: "Cómo hablar"
- Nivel de control (básico/pro)

### Modo Pro (expandible)
- Mostrar internals:
  - tools permitidas
  - mcp allowlist
  - output schema
  - fallback policies

---

## 5) Integración con pantallas existentes

## A) ChannelsPage
Agregar en acciones del canal:
- `Cargar intenciones` (ya implementado)
- `Crear automatización desde este canal` (nuevo CTA)

Comportamiento:
- Prellena canal en Wizard.
- Sugiere intenciones por tipo de canal.

## B) IntentsPage
Evolución:
- Vista por negocio: "Qué quiere decir el cliente".
- Mostrar "Confianza de detección" y "No match recientes".
- CTA: `Probar con mensaje real` y `Crear intención sugerida`.

## C) WorkflowsPage
Evolución:
- Cambiar framing a "Automatizaciones".
- Tarjetas con objetivo: "Venta por WhatsApp", "Cobro con factura".
- Mostrar estado de readiness:
  - Canal listo
  - Intenciones cargadas
  - Subflujo validado

## D) CommerceAdminPage (Ventas y cobros)
Ya alineado por tabs condicionados por módulos.
Siguiente mejora:
- Desde wizard, deep-link al tab requerido cuando falta módulo.

## E) Config Assistant
Convertir en copiloto persistente:
- "Ayúdame a terminar esto"
- Diagnóstico con acciones ejecutables:
  - "Asignar router"
  - "Cargar intenciones recomendadas"
  - "Publicar workflow"

---

## 6) Wireframes low-fi (texto)

## 6.1 Pantalla "Crear automatización"

```text
+-------------------------------------------------------------+
| Crear automatización                                        |
| ¿Qué quieres lograr hoy?                                    |
| [ Quiero vender por WhatsApp y cobrar con factura ... ]     |
| [Vender] [Cobrar] [Soporte] [Agendar] [Seguimiento]         |
|                                                             |
| Siguiente ->                                                 |
+-------------------------------------------------------------+
```

## 6.2 Paso canal

```text
+-------------------------------------------------------------+
| Paso 2/5: Canal                                              |
| Canal seleccionado: WhatsApp Soporte [Healthy]               |
| [Cambiar canal] [Activar] [Health]                           |
|                                                             |
| Si no está listo: "Resolver ahora"                          |
|                                                             |
| <- Atrás                                   Siguiente ->      |
+-------------------------------------------------------------+
```

## 6.3 Paso datos

```text
+-------------------------------------------------------------+
| Paso 3/5: Datos a recopilar                                  |
| ¿Qué necesitas preguntar al cliente?                         |
| [ + Nombre ] [ + Teléfono ] [ + Producto ] [ + Cantidad ]   |
|                                                             |
| Lista actual:                                                |
| 1. Nombre completo (requerido)                               |
| 2. Producto (requerido)                                      |
| 3. Cantidad (requerido)                                      |
|                                                             |
| <- Atrás                                   Siguiente ->      |
+-------------------------------------------------------------+
```

## 6.4 Paso acciones finales

```text
+-------------------------------------------------------------+
| Paso 4/5: Qué debe hacer el sistema                          |
| [x] Crear venta                                               |
| [x] Generar factura                                           |
| [ ] Escalar humano                                            |
| [x] Enviar confirmación                                       |
|                                                             |
| <- Atrás                                   Siguiente ->      |
+-------------------------------------------------------------+
```

## 6.5 Simulación y publish

```text
+-------------------------------------------------------------+
| Paso 5/5: Simular y publicar                                 |
| Cliente: "Quiero 2 camisas talla M"                         |
| Sistema: "Perfecto, ¿a nombre de quién va la compra?"       |
|                                                             |
| Entendí: intención "crear_venta"                            |
| Subflujo: "Cobro y facturación"                              |
| Acción final: "Venta + Factura"                              |
|                                                             |
| [Guardar borrador] [Piloto 10%] [Publicar]                   |
+-------------------------------------------------------------+
```

---

## 7) Microcopy recomendado (español simple)

- En vez de "Intent mapping": **"Qué quiere decir el cliente"**
- En vez de "Output schema": **"Datos que el sistema debe entregar"**
- En vez de "Tool allowlist": **"Acciones permitidas"**
- En vez de "MCP server": **"Integraciones conectadas"**

Mensajes de error -> acción:
- "No hay intenciones cargadas" + botón `Cargar recomendadas`
- "Canal no activo" + botón `Activar canal`
- "Módulo no habilitado" + botón `Habilitar módulo`

---

## 8) Roadmap de implementación (sobre código actual)

## Fase 1 (rápida, 1-2 sprints)
1. Crear pantalla/diálogo `Crear automatización` como wizard.
2. Integrar prefill desde `ChannelsPage`.
3. Integrar selección intenciones usando endpoint de catálogo por canal.
4. Agregar simulación básica con endpoint classify + preview routing.

## Fase 2
1. Generación automática de subflujo por objetivo.
2. Copiloto persistente en Workflows/Channels.
3. Readiness score por automatización.

## Fase 3
1. Publicación progresiva (piloto por porcentaje).
2. Recomendaciones automáticas por no-match y baja confianza.
3. Modo pro consolidado (tools/MCP/schema con validaciones).

---

## 9) KPIs UX del cambio

1. Time-to-first-automation publicada.
2. % usuarios que completan wizard sin soporte.
3. % workflows que pasan de borrador a piloto.
4. Reducción de "no_match" en intenciones.
5. Conversión de piloto a producción.

---

## 10) Qué ya tienen y debemos capitalizar

1. Routing por canal + catálogo de intenciones por canal.
2. Config Assistant con herramientas de diagnóstico.
3. Páginas separadas para canales, intenciones, workflows y commerce.
4. Base para subflujos en `WorkflowSteps` de `AgentDefinition`.

La propuesta no rompe arquitectura actual: encapsula complejidad detrás de una UX guiada y progresiva.
