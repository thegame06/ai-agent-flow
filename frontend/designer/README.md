# AgentFlow Studio (frontend/designer)

AgentFlow Studio es el producto oficial para diseño visual de agentes dentro del frontend de AgentFlow.

## Qué incluye Studio

- Navegación dedicada (`/studio/:id`) dentro del shell principal.
- Canvas como eje central para modelar flujos con nodos estandarizados y transiciones explícitas.
- Validaciones de diseño en tiempo real:
  - nodos sin salida,
  - referencias de variables inválidas (`{{variable}}`),
  - nodos sin conexión.
- Simulación guiada step-by-step con contexto de variables en cada paso.
- Señalización de permisos de Studio (`studio.view`, `studio.edit`, `studio.publish`) en el header.

## Permisos de Studio

Studio lee permisos desde `localStorage` en la clave `studio_permissions`.

Ejemplo:

```json
["studio.view", "studio.edit", "studio.publish"]
```

## Guía para usuarios no técnicos

Revisar la guía publicada en:

- `docs/STUDIO-USER-GUIDE.md`
