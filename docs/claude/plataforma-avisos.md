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

### Endpoints que no llevaban [Authorize] (auditoría de seguridad, 26/11/26)
- Cerrados (`[Authorize]`), porque todos sus llamantes conocidos mandan token: `GET api/ExtractosCliente/DeudaVencida` (Nesto, ficha de cliente, por
  `ClienteApiFactory`) y `GET api/AlbaranesVenta` (Nesto, `PedidoVentaService.CargarAlbaran`).
- SIN cerrar a propósito: `GET api/ExtractosCliente/EnviarCorreoDomiciliacionDia` (ningún repositorio ni job lo llama: será una tarea programada de
  fuera, quizá sin token) y `PUT api/ParametrosUsuario` (Nesto guarda sus preferencias con `Configuracion.GuardarParametro`/`GuardarParametroSync`, un
  HttpClient SIN token; NestoApp sí lo manda). En el PUT, LA REGLA (pura) `ReglaCambioParametroUsuario` (`Infraestructure/ParametrosUsuario/`): las claves
  de permiso (`PermitirCrearPedidoConErroresValidacion`, `InventarioBorrarFilas` y las que empiezan por `Permitir`, `Cupones.`, `NestoViejo_`,
  `Validacion.`) y la fila «(defecto)» solo Dirección/Informática con token; un usuario identificado solo SUS filas; sin token, lo de siempre (sus
  preferencias) salvo esas → 403 de usted. Para cerrarlo del todo: que Nesto guarde por `ClienteApiFactory` y entonces `[Authorize]`.

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
