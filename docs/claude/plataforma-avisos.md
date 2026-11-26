# Plataforma: avisos, correos y ELMAH

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Notificaciones urgentes, remitente de los correos, novedades, usuario y credenciales en ELMAH.
Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.

### Notificaciones urgentes (NestoAPI#775, 31/10/26)
- La urgencia va **por tipo**, en código: lista blanca `NotificacionesUrgentes.TIPOS` (`Infraestructure/Notificaciones/NotificacionesUrgentes.cs`).
  Hoy solo `AutorizacionReembolsoReparto` (#773). Al guardarse en el buzón de Nesto, además de `hayNotificacionesNuevas` se manda
  `hayNotificacionUrgente` (AvisosHub) y Nesto abre una ventana roja con pitido. Datos["referencia"] identifica lo que hay que contestar.
- Al contestarse: `notificacionResuelta` {Tipo, Referencia, Respondio, Resultado} a todos los destinatarios → se cierra en todos los Nesto.
- `GET api/Notificaciones/Urgentes`: las urgentes del buzón del usuario (24 h) que siguen pendientes (cada tipo con su
  `IComprobadorUrgentesPendientes`). Nesto lo pide al arrancar y en cada (re)conexión; sin sondeo.
- Autorización de reembolso: el primer aviso va SOLO a Administración; a los 2 min (`BackgroundJob.Schedule`,
  `EscaladoAutorizacionesReembolsoJobs`) si sigue Pendiente va también a Dirección (menos los que ya son de Administración) y vuelve a sonar
  en Administración. La revisión de las telefónicas sigue yendo a las dos. `POST …/Envios/{id}/Autorizacion` devuelve `Conectados`
  (Administración con Nesto abierto, `UsuariosConectadosNesto.Conectados`) y `ConectadosNombres`.

### Correos mandados a mano a nombre de quien los manda (sugerencia 1167, 11/11/26)
- Pedido, presupuesto y mandato (`GestorEnvioDocumentosCorreo`) y facturas A MANO (`GestorFacturas.EnviarFacturasACorreo`, vía
  `RemitenteDelUsuario`, que solo pone `FacturasController`): From con la MISMA dirección de la serie (Office 365 autenticado rechaza
  otra) y nombre «{Nombre} - Nueva Visión»; Reply-To y copia a su CorreoDefecto; firma con su nombre, el teléfono de la firma de la
  serie y su correo. Nombre = `Vendedores.Descripción` del vendedor (claim «Vendedor» o ParametrosUsuario «Vendedor»), si no, el usuario.
  El envío diario y la factura de Verifactu no cambian.

### Correos y WhatsApp a nombre de quien los manda: flecos (NestoAPI#854, NestoApp#272, 11/11/26)
- `ServicioCorreoElectronico.EnviarCorreoSMTP` apunta en ELMAH el correo que el SMTP rechaza tras el reintento (asunto, From, To, motivo).
- `ProveedorDocumentosCorreo.NombreVendedorDelUsuario` (claim «Vendedor» o parámetro «Vendedor»; null si no se conoce, SIN caer al usuario):
  firma del WhatsApp (`GestorEnlacesDescarga.Firma`, `EnlaceDescargaDTO.Remitente`). `FacturasController` pasa ya el claim al remitente.

### Novedades por Id (NestoAPI#495, 11/11/26)
- `GET api/Novedades?ambitos=NestoApp,NestoAPI&desdeId=n` (`ambitos` manda sobre `ambito`; sigue el filtro por perfiles). Parámetros
  `UltimaNovedadVistaApp`/`UltimaNovedadVista` por ParametrosUsuario (`Scripts/Issue495_UltimaNovedadVista.sql`). Una sugerencia implementada
  conserva su Id antiguo: con `desdeId` no sale. NestoApp ya lo usa (2.28.0); el lado Nesto, pendiente.

### Novedades de una versión por la API, sin scripts de sa (NestoAPI#890, 26/11/26)
- En vez de `Scripts/Novedades_X_Y_Z_R.sql`: `Scripts/PublicarNovedades.ps1 -Version 1.10.68.0 -Fichero novedades.json` (ensayo, pregunta y carga;
  token de Windows). Todo Dirección/Informática (403 de usted), errores {Message} con su estado. `ServicioNovedadesVersion` + LAS REGLAS (puras)
  `ReglasNovedadesVersion` (`Infraestructure/Novedades/NovedadesVersion.cs`), SQL en `RepositorioNovedadesVersionSql`.
- `POST api/Novedades/Version?ensayo=false` {Version, Fecha? (hoy), Ambito Nesto|NestoAPI|NestoApp|Ariadna (1.x escritorio, 2.x apps: si no
  cuadra, 400), Novedades [{Categoria Nuevo|Mejorado|Corregido, Titulo ≤200, Descripcion ≤1000, Perfiles?, SugerenciaId?}] (1..100, títulos sin
  repetir)} → {Version, Ambito, Fecha, Ensayo, Creadas, YaEstaban, SugerenciasImplementadas [{Id, SugerenciaId, Categoria, Titulo}], Mensaje}. Todo se
  valida antes de escribir; idempotente por Versión + Ámbito + Título (INSERT … WHERE NOT EXISTS, applock `NovedadesVersion:{ámbito}:{versión}`, una
  transacción y UNA fila en Modificaciones, Tabla `Novedades`, Anterior «NovedadesVersion|ámbito|versión»). Con `SugerenciaId` no crea otra: la pasa a
  Implementada por el MISMO camino que `PUT api/Novedades/Sugerencias/{id}` (y sus perfiles si vienen; rastro «SugerenciaImplementada|id»); ya
  implementada en esta versión = YaEstaban; en otra, 409; no existe o no es sugerencia, 400.
- `GET api/Novedades/Version?version=&ambito=` (la versión va en la consulta: con puntos en la ruta IIS no la sirve) → [{Id, Version, Fecha,
  Categoria, Titulo, Descripcion, Ambito, Publicada, Perfiles, EsSugerencia}]. `PUT api/Novedades/{id}` {Titulo?, Descripcion?, Categoria?, Perfiles?,
  Publicada?} (null = no tocar; solo filas con versión: una sugerencia sin implementar → 409, va por su PUT; título repetido en la versión → 409) →
  la fila. `DELETE api/Novedades/{id}` → 204; nunca una sugerencia ni una con comentarios o votos (409: despublíquela); con adjuntos/capturas/vídeo
  (FK) también 409. Rastro en Modificaciones (antes y después). `Scripts/Issue890_NovedadesGrants.sql` (sa): el DELETE en dbo.Novedades.

### Endpoints que no llevaban [Authorize] (auditoría de seguridad, 26/11/26)
- Cerrados (`[Authorize]`), porque todos sus llamantes conocidos mandan token: `GET api/ExtractosCliente/DeudaVencida` (Nesto, ficha de cliente, por
  `ClienteApiFactory`) y `GET api/AlbaranesVenta` (Nesto, `PedidoVentaService.CargarAlbaran`).
- SIN cerrar a propósito: `GET api/ExtractosCliente/EnviarCorreoDomiciliacionDia` (ningún repositorio ni job lo llama: será una tarea programada de
  fuera, quizá sin token) y `PUT api/ParametrosUsuario` (Nesto guarda sus preferencias con `Configuracion.GuardarParametro`/`GuardarParametroSync`, un
  HttpClient SIN token; NestoApp sí lo manda). En el PUT, LA REGLA (pura) `ReglaCambioParametroUsuario` (`Infraestructure/ParametrosUsuario/`): las claves
  de permiso (`PermitirCrearPedidoConErroresValidacion`, `InventarioBorrarFilas` y las que empiezan por `Permitir`, `Cupones.`, `NestoViejo_`,
  `Validacion.`) y la fila «(defecto)» solo Dirección/Informática con token; un usuario identificado solo SUS filas; sin token, lo de siempre (sus
  preferencias) salvo esas → 403 de usted. Para cerrarlo del todo: que Nesto guarde por `ClienteApiFactory` y entonces `[Authorize]`.
- **El PUT ya no da 404 a un parámetro con «(defecto)» (incidencia 1481, Lidia 26/11/26)**: la casilla de los borradores de Nesto decía «No se ha
  podido guardar su preferencia» porque el PUT solo MODIFICABA y el usuario aún no tenía su fila de `PlantillaBorradorActualizarCargado` (tampoco había
  «(defecto)»: la crea el script del commit 998a7ad9). Ahora, DESPUÉS de la regla de permisos, LA REGLA (pura) `ReglaGuardarParametroUsuario.Decidir`
  (`Infraestructure/ParametrosUsuario/GuardadoParametroUsuario.cs`): fila del usuario → se modifica (como siempre); sin ella pero con la «(defecto)» de la
  clave → se CREA la del usuario con el valor pedido (Usuario2 = el enviado o el usuario; fecha = la enviada o ahora), 204; sin ninguna → 404 de usted
  «El parámetro X no está dado de alta: avise a Informática.» y una línea en ELMAH por empresa + clave y día (en memoria, `AvisoParametroNoDadoDeAlta`).
  Núcleo `ServicioGuardarParametroUsuario` sobre `IAlmacenParametrosUsuario` (EF en producción; `AlmacenParametros` sustituible en los tests).

### ELMAH User Sync (05/12/25)
ELMAH no mostraba el usuario en los errores porque OWIN/JWT no sincroniza automáticamente el usuario con `HttpContext.Current.User`.

**Solución implementada**:
1. **`UserSyncHandler.cs`**: DelegatingHandler que copia `request.GetRequestContext().Principal` a `HttpContext.Current.User`
2. **`WebApiConfig.cs`**: Registra el handler con `config.MessageHandlers.Add(new UserSyncHandler())`

**IMPORTANTE - NO usar TokenValidationParameters (09/12/25)**:
El commit c4cbbd1 añadió `TokenValidationParameters` a `JwtBearerAuthenticationOptions`, pero esto causaba
que OWIN ignorara `AllowedAudiences` e `IssuerSecurityKeyProviders`, rechazando todos los tokens JWT.
Ver `StartupJwtConfigurationTests.cs` para detalles del bug y su documentación.

**Flujos de autenticación soportados**:
| Aplicación | Endpoint | Usuario en ELMAH |
|------------|----------|------------------|
| NestoApp (vendedores) | `/oauth/token` | `UserName` de Identity |
| Nesto (empleados) | `/api/auth/windows-token` | `DOMINIO\Usuario` |
| TiendasNuevaVision (clientes) | `/api/auth/token` | `email` |

**ELMAH sin credenciales (25/11/26)**: el `errorLog` del Web.config es `NestoAPI.Infraestructure.ErrorLogSinCredenciales, NestoAPI` (hereda de
`SqlErrorLog`; volver atrás = `type="Elmah.SqlErrorLog, Elmah"`). Antes de guardar, LA REGLA (pura) `ReglaElmahSinCredenciales` (`Infraestructure/
ElmahSinCredenciales.cs`) oculta en una COPIA del error las variables, campos del formulario y parámetros cuyo nombre contiene AUTHORIZATION, COOKIE, TOKEN,
SECRET, PASSWORD o APIKEY («(oculto)»; Authorization conserva el esquema: «Bearer (oculto)»), esas líneas dentro de ALL_HTTP/ALL_RAW, los parámetros sensibles de
QUERY_STRING y el Referer, y todas las cookies. Nunca lanza (si falla, se guarda tal cual). Los errores ya guardados no se tocan.

Related files:
- `Infraestructure/UserSyncHandler.cs` - DelegatingHandler para sincronizar usuario
- `App_Start/WebApiConfig.cs` - Registro del handler
- `StartupJwtConfigurationTests.cs` - Documentación del bug de TokenValidationParameters
