# Guía de uso de AgentFlow Studio (No técnica)

## 1) ¿Qué es Studio?

Studio es el editor visual oficial de AgentFlow para construir automatizaciones con IA sin programar.

## 2) Flujo recomendado en 5 pasos

1. **Crear un flujo** desde `Studio`.
2. **Arrastrar nodos** al canvas (razonamiento, herramientas, revisión humana, salida).
3. **Conectar transiciones** entre nodos.
4. **Revisar validaciones** para corregir errores de diseño.
5. **Simular paso a paso** y luego publicar.

## 3) Cómo leer las validaciones

- **Nodo sin salida**: una parte del flujo se puede quedar atascada.
- **Variable inválida**: se usa una variable no definida en el diseño.
- **Nodo sin conexión**: existe un bloque que nunca se ejecutará.

## 4) Simulación guiada

La simulación muestra:

- nodo actual del flujo,
- orden de ejecución,
- valores de variables activas en cada paso.

Úsala para validar la lógica antes de producción.

## 5) Ejemplos por industria

### Banca

- Entrada: solicitud de crédito.
- Nodos: scoring → validación documental → revisión manual (si riesgo alto) → respuesta.
- Variables comunes: `riskScore`, `incomeBand`, `decision`.

### Seguros

- Entrada: reporte de siniestro.
- Nodos: extracción de datos → fraude inicial → ajuste de póliza → aprobación/rechazo.
- Variables comunes: `claimAmount`, `fraudFlag`, `coverageStatus`.

### Salud

- Entrada: solicitud de autorización.
- Nodos: elegibilidad → políticas clínicas → revisión humana → notificación.
- Variables comunes: `memberStatus`, `clinicalRuleMatch`, `authorizationResult`.

### Retail / eCommerce

- Entrada: consulta post-venta.
- Nodos: clasificación de intención → consulta de pedido → política de devoluciones → resolución.
- Variables comunes: `orderStatus`, `returnWindow`, `nextAction`.

## 6) Buenas prácticas

- Diseña primero el “camino feliz”.
- Agrega siempre una salida clara por cada nodo.
- Reserva revisión humana para casos de riesgo.
- Simula con 2-3 escenarios reales antes de publicar.
