# Propuesta UX, Branding y Reagrupacion del Menu

## Objetivo
Reducir saturacion en la navegacion y separar con claridad:

1. Lo que usa el usuario final u operador en el dia a dia.
2. Lo que usa un configurador o implementador.
3. Lo que pertenece a administracion avanzada, integraciones o gobierno.

La meta no es solo "ordenar el menu". La meta es cambiar la percepcion del producto de "plataforma tecnica con muchas pantallas" a "centro de operaciones con configuracion guiada".

---

## Diagnostico del estado actual

La navegacion actual mezcla en el mismo nivel cuatro tipos de trabajo:

1. Creacion de automatizaciones.
2. Operacion diaria.
3. Integraciones y herramientas tecnicas.
4. Configuracion avanzada y gobierno.

Problemas detectados en la implementacion actual:

1. Hay demasiadas entradas primarias para un mismo flujo mental.
   - `Configuracion asistida`
   - `Flujos automatizados`
   - `Runtime Studio`
   - `Asistentes IA`
   - `Reglas de intencion`

2. El menu mezcla lenguaje de negocio y lenguaje tecnico.
   - "Ventas y cobros" convive con "MCP", "Feature Flags", "Auth Profiles", "Runtime Studio".

3. El branding y el tono no son consistentes.
   - Conviven `AnnonAI`, `AgentFlow`, "Platform Settings", "Settings", "Feature Flags", "Configuracion asistida".

4. Hay duplicidad conceptual en herramientas e integraciones.
   - `Marketplace`
   - `Herramientas e integraciones`
   - `Herramientas externas`
   - `Canales`

5. Se expone complejidad de arquitectura demasiado pronto.
   - MCP, runtime, tools, auth profiles, operations, policies y audit aparecen cerca del trabajo principal.

Conclusión: el menu esta organizado por como fue construido el sistema, no por como lo usa el cliente.

---

## Segmentacion de usuarios

### 1. Operador / usuario final
Usa la plataforma para atender, vender, revisar casos y monitorear resultados.

Necesita:

1. Bandeja
2. Casos por revisar
3. Ventas y cobros
4. Actividad
5. Reportes basicos

No necesita ver:

1. MCP
2. Feature Flags
3. Auth Profiles
4. Runtime Studio
5. Policies
6. Audit

### 2. Configurador / implementador
Diseña la automatizacion, conecta canales y ajusta intenciones.

Necesita:

1. Crear automatizacion
2. Automatizaciones
3. Canales
4. Intenciones
5. Asistentes
6. Integraciones

### 3. Administrador tecnico
Gestiona modelos, credenciales, permisos, observabilidad y gobierno.

Necesita:

1. Modelos IA
2. Credenciales
3. Politicas
4. Auditoria
5. Operaciones IA
6. Feature Flags
7. Workforce
8. Herramientas externas

---

## Propuesta de arquitectura de navegacion

## Regla principal
Maximo 5 entradas primarias visibles en sidebar principal.

El resto debe vivir en:

1. Vistas secundarias
2. Tabs internos
3. Panel "Administracion"
4. Modo avanzado

## Nueva estructura recomendada

### Grupo 1: Inicio
Entry point para orientacion y estado general.

Items:

1. Inicio

Rol UX:

1. Mostrar estado de preparacion
2. Mostrar accesos rapidos
3. Llevar al flujo principal de crear automatizacion

### Grupo 2: Operacion
Solo lo que se usa todos los dias.

Items:

1. Bandeja
2. Casos por revisar
3. Ventas y cobros
4. Actividad

Opcional:

1. KYC y pagos
   - Solo si el modulo esta habilitado.

### Grupo 3: Construccion
Espacio para crear y mejorar automatizaciones.

Items:

1. Crear automatizacion
2. Automatizaciones
3. Asistentes
4. Intenciones

Mover fuera del menu principal:

1. Runtime Studio
   - Debe quedar dentro de Automatizaciones o Asistentes como modo avanzado.

### Grupo 4: Conexiones
Todo lo que conecta la plataforma con el exterior.

Items:

1. Canales
2. Integraciones
3. Herramientas

Cambio semantico:

1. `Marketplace` pasa a llamarse `Integraciones`
2. `Herramientas externas` pasa a ser subnivel de `Herramientas`
3. `MCP` deja de ser nombre visible para usuario no tecnico

### Grupo 5: Administracion
Acceso restringido por rol. Debe sentirse como backoffice, no como trabajo diario.

Items:

1. Configuracion general
2. Modelos IA
3. Credenciales
4. Equipos y atencion
5. Politicas
6. Auditoria
7. Operaciones IA
8. Funciones beta

Regla:

1. Este grupo debe ir colapsado por defecto.

---

## Menu propuesto

```text
Inicio

Operacion
- Bandeja
- Casos por revisar
- Ventas y cobros
- Actividad
- KYC y pagos (si aplica)

Construccion
- Crear automatizacion
- Automatizaciones
- Asistentes
- Intenciones

Conexiones
- Canales
- Integraciones
- Herramientas

Administracion
- Configuracion general
- Modelos IA
- Credenciales
- Equipos y atencion
- Politicas
- Auditoria
- Operaciones IA
- Funciones beta
```

---

## Que debe desaparecer del primer nivel

Estas etiquetas no deberian seguir visibles como items principales:

1. Runtime Studio
2. MCP
3. Feature Flags
4. Auth Profiles
5. Tools
6. Settings
7. AnnonAI como subheader

Razon:

1. Son terminos internos o tecnicos.
2. No expresan objetivo de negocio.
3. Compiten por atencion con tareas de mayor frecuencia.

---

## Propuesta de branding

## Problema actual
La interfaz hoy transmite varias identidades al mismo tiempo:

1. `AgentFlow`
2. `AnnonAI`
3. Terminologia inglesa en paginas clave
4. Terminologia tecnica de arquitectura

Eso debilita recordacion de marca y genera sensacion de producto inacabado.

## Recomendacion
Definir una marca visible y una marca tecnica.

### Marca visible
La marca que ve el cliente en toda la experiencia.

Recomendacion:

1. Elegir una sola:
   - `AnnonAI`
   - o `AgentFlow`

No usar ambas al mismo tiempo en UI.

### Marca tecnica
Puede seguir existiendo en repositorio, namespaces y backend.

Ejemplo:

1. Marca comercial en UI: `AnnonAI`
2. Marca tecnica interna: `AgentFlow Platform`

## Personalidad de marca recomendada

1. Clara
2. Operativa
3. Confiable
4. Asistida
5. Poco tecnica en fachada, potente en profundidad

## Territorio verbal

Usar verbos de accion y lenguaje orientado a resultado:

1. Crear automatizacion
2. Conectar canal
3. Revisar casos
4. Activar integracion
5. Publicar cambio

Evitar en interfaz principal:

1. Runtime
2. MCP
3. Auth Profile
4. Execution Replay
5. Prompt Injection Guard

Esos terminos pueden existir en modo avanzado.

---

## Sistema de naming

## Regla de naming
Cada nombre debe responder una de estas tres preguntas:

1. Que hago aqui.
2. Que resultado obtengo.
3. Que area administro.

## Renombres recomendados

1. `Configuracion asistida` -> `Crear automatizacion`
2. `Flujos automatizados` -> `Automatizaciones`
3. `Asistentes IA` -> `Asistentes`
4. `Reglas de intencion` -> `Intenciones`
5. `Canales de atencion` -> `Canales`
6. `Herramientas e integraciones` -> `Herramientas`
7. `Herramientas externas` -> `Conectores avanzados`
8. `Credenciales` en vez de `Auth Profiles`
9. `Funciones beta` en vez de `Feature Flags`
10. `Configuracion general` en vez de `Settings`
11. `Actividad` en vez de `Ejecuciones`
12. `Casos por revisar` se mantiene

---

## Modelo de agrupacion de herramientas

El usuario pidio aislar lo que si usa el usuario final. Para eso propongo 3 niveles.

## Nivel 1: Herramientas de negocio
Visibles para usuario funcional.

1. Crear venta
2. Generar factura
3. Consultar pedido
4. Escalar a humano
5. Enviar confirmacion
6. Validar pago

## Nivel 2: Herramientas de canal y conexion
Visibles para implementador.

1. WhatsApp
2. Web chat
3. API
4. ERP
5. CRM
6. Pasarela de pagos

## Nivel 3: Herramientas tecnicas
Ocultas por defecto.

1. MCP
2. Tool testing manual
3. JSON input schema
4. Health checks detallados
5. Toggles por herramienta

## Regla UX

1. El usuario final nunca debe entrar a una tabla de herramientas con JSON.
2. Debe consumir acciones empaquetadas por objetivo.
3. La capa tecnica se reserva para administracion avanzada.

---

## Patron de experiencia recomendado

## 1. Inicio orientado a accion
La home debe responder:

1. Que me falta para operar
2. Que debo hacer ahora
3. Que esta bloqueado
4. Cual es mi siguiente paso

## 2. Sidebar minimalista
Pocas categorias, texto corto, sin tecnicismos.

## 3. Header contextual
Cada pagina debe mostrar:

1. Objetivo de la seccion
2. Estado
3. CTA principal

Ejemplo:

1. `Automatizaciones`
   - "Crea, prueba y publica experiencias automatizadas"
   - CTA: `Nueva automatizacion`

## 4. Progresive disclosure
Modo basico por defecto.
Modo avanzado expandible.

## 5. Menus por rol
La saturacion no se resuelve solo agrupando.
Tambien se resuelve ocultando por rol.

Regla:

1. Operador no ve administracion.
2. Implementador ve construccion y conexiones.
3. Admin tecnico ve todo.

---

## Direccion visual recomendada

## Estilo

1. Base clara
2. Jerarquia fuerte
3. Sensacion de centro de control
4. Menos "template genérico", mas "producto operativo"

## UI tokens sugeridos

### Colores

1. Primario: verde petroleo o azul petroleo
   - transmite confianza operativa
2. Secundario: arena o gris calido
   - baja ruido visual
3. Estados:
   - exito: verde controlado
   - alerta: ambar
   - error: rojo sobrio

### Tipografia

1. Una sola familia para producto completo
2. Mejor una sans sobria y con personalidad
3. Headers cortos, densos y consistentes

### Iconografia

1. Una sola familia
2. Menos iconos decorativos
3. Mas uso de estados y etiquetas

### Layout

1. Sidebar mas limpio
2. Headers con resumen y CTA
3. Cards con menos contorno y mas jerarquia por contenido
4. Menos tablas en pantallas principales

---

## Quick wins de alto impacto

## Sprint 1

1. Unificar marca visible en toda la UI.
2. Renombrar labels mezclados ingles/espanol.
3. Reordenar sidebar en 5 grupos.
4. Colapsar Administracion por defecto.
5. Mover `MCP`, `Feature Flags`, `Auth Profiles` y `Runtime Studio` fuera del primer nivel.

## Sprint 2

1. Separar vistas por rol.
2. Convertir `Herramientas e integraciones` en experiencia de catalogo, no tabla tecnica.
3. Hacer que `Crear automatizacion` sea el CTA principal global.
4. Agregar estados de readiness en Inicio, Canales y Automatizaciones.

## Sprint 3

1. Consolidar modo avanzado.
2. Replantear visualmente la home como centro operativo.
3. Ocultar detalles tecnicos detras de drawers o tabs avanzados.

---

## Recomendacion concreta de implementacion

No empezaria por rediseñar todas las paginas.

Empezaria por este orden:

1. Navegacion
2. Naming
3. Roles y visibilidad
4. Home
5. Herramientas / integraciones

La razon es simple:

1. La saturacion principal hoy esta en la arquitectura de descubrimiento.
2. Si arreglas primero paginas individuales sin arreglar menu y taxonomia, el problema persiste.

---

## Decision recomendada

Si el objetivo es aislar lo que realmente usa el usuario final, la estructura correcta es:

1. `Operacion` como espacio principal del usuario final.
2. `Construccion` como espacio de configuracion guiada.
3. `Conexiones` como espacio de implementacion.
4. `Administracion` como capa secundaria y restringida.

Eso reduce ruido, mejora onboarding y alinea el producto con tareas reales en lugar de modulos tecnicos.
