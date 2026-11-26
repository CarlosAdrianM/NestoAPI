# Compras

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Correos de seguimiento (#857), plazos y fechas de recepción, certeza, próxima entrada.
Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.

### Correos de seguimiento de compras: fase 1 en sombra y fase 3 (NestoAPI#857, 12/11/26)
- Job `compras-correos-seguimiento` (cada 30 min, L-V 8:00-18:30 Madrid, `Infraestructure/PedidosCompra/CorreosSeguimiento/`): lee por
  Microsoft Graph (HttpClient, client credentials, SOLO lectura: nunca marca ni mueve) la bandeja de entrada del buzón de Compras con delta
  (`ComprasCorreosSeguimientoEstado`; sin enlace, los últimos 7 días; 410 = empezar de nuevo). Claves en secretos.config `ComprasBuzonTenant`,
  `ComprasBuzonClientId`, `ComprasBuzonSecreto`, `ComprasBuzon`: sin ellas no hace nada ni avisa. Sin `Scripts/Issue857_ComprasCorreosSeguimiento.sql`,
  tampoco (ELMAH una vez). 401/429/red: ELMAH y sigue desde la misma página en la siguiente pasada; el delta solo avanza si todo se guardó.
- Guarda en `ComprasCorreosSeguimiento` (idempotente por MensajeId) SOLO los correos con seguimiento o de un remitente marcado
  (`AgenciasEntradaRemitentes`): agencia, primer código/URL (JSON con todos), extracto de 500, nada de cuerpo ni adjuntos. Agencias:
  `IAgenciaEntrada` (núcleo `AgenciaEntradaPorPatrones` + una clase de datos por agencia en `AgenciasEntradaRegistradas`, `HorasHastaEntrega`
  24/48) + `AgenciaEntradaGenerica` (descubre: agencia NULL). Enlaces desenvueltos sin peticiones (safelinks, Proofpoint v2, google.com/url, redirect=…).
  Lo que capta el genérico (sin remitente marcado) lleva la agencia que el texto NOMBRE tras una etiqueta («Transportista:», «Enviado por:», «Agencia:»,
  «Carrier:», «Shipped via»…; `AgenciaPorNombreEnTexto`, `NombresEnTexto` de cada agencia, 40 caracteres de esa línea, gana el nombre más largo); el nombre
  suelto no cuenta. `AnalisisCorreoSeguimiento.AgenciaPorNombreEnTexto` no se guarda (sin columna). MBE (Mail Boxes Etc) es agencia registrada.
- Asignación en sombra (`AsignadorPedidosCompra`, pura): número de pedido nuestro > dirección de un contacto > dominio (web/contactos; nunca
  gratuito, nuestro o de agencia) > nombre del proveedor; con proveedor, TODOS sus pedidos en el aire (-1/1 Enviado) son candidatos. El job
  rellena PedidoReal/FechaAlbaranReal con el primer candidato albaraneado desde el día del correo (para medir). No toca compras (fase 3).
- `GET/POST/DELETE api/Compras/AgenciasEntrada/Remitentes`, `GET …/Candidatos?dias=30`, `GET …/Correos?dias=7` (Compras/Dirección/Informática).
- **Histórico** (`ServicioHistoricoCorreosCompras`): `POST …/Historico?meses=12[&ahora=true]` (Dirección/Informática; 1-24; 202 encolado en Hangfire,
  409 si hay otra viva, 503 sin claves o tablas) y `GET …/Historico` (progreso). Lee la bandeja por fechas (`LeerPagina`, sin delta) con el MISMO
  núcleo (`GuardarCorreos`), asignando contra los pedidos en el aire EL DÍA del correo (`EnAireEl`) y midiendo por lotes por Id. Progreso en
  `ComprasCorreosSeguimientoEstado` fila `historico|{buzón}` (JSON; reanuda por cursor de fecha); 429 → Retry-After (tope 5 min); 5 fallos → Fallo + ELMAH una vez.
  L-V 8-20 Madrid no empieza: se programa a las 21:00 (salvo `ahora`).
- **Adjuntos** (`Scripts/Issue857b_ComprasCorreosHistoricoYAdjuntos.sql`: `NumAdjuntos`, `Adjuntos` JSON de 10, sin los incrustados): una petición a Graph
  solo por correo GUARDADO con `hasAttachments`; sin las columnas (caché 10 min) se guarda como antes y `…/Correos` los da a null.
- **Entrega estimada y salidas nuestras** (`EntregaEstimadaCorreo.cs`, `Scripts/Issue857c_ComprasCorreosEntregaEstimada.sql`, 5 columnas opcionales con
  el mismo trato que los adjuntos): `ExtractorEntregaEstimada` (puro) saca día y tramo tras una etiqueta o frase («Entrega estimada», «te lo entregaremos,
  como máximo, el…», «will be delivered on…»; una fecha suelta no cuenta; fuera de [día del correo, +60] nada). `ReglaDisponibleCompra`: manda el FINAL del tramo
  (Carlos 12/11/26): antes del corte del picking en laborable de Algete, ese día; si no, el siguiente laborable. `EnvioNuestroDeSalida` («tu pedido … de NUEVA
  VISION», «remitente: NUEVA VISION»…; nombrarnos como destinatario no cuenta): se guarda con `EsSalidaNuestra` y SIN proveedor, candidatos ni disponible.
- **FASE 3: ya NO es solo sombra** (`Scripts/Issue857d_ComprasCorreosAplicarFecha.sql`; sin él, todo como antes y los endpoints nuevos 503). El job pone
  `LinPedidoCmp.FechaRecepción` (+Usuario «Correo de seguimiento», [Fecha Modificación]) de TODAS las líneas en el aire del pedido si el envío es
  inequívoco (un solo candidato), el correo es de los últimos 3 días y ParametrosUsuario «(defecto)» `Compras.CorreosSeguimientoAplicar` = «1» (si no,
  no escribe). Fecha = DÍA DE LLEGADA (`ReglaFechaRecepcionPorCorreo`: la entrega más tardía del envío o recibido + 24/48 h; nunca antes de hoy), no el
  disponible. Pisa lo que hubiera; misma fecha → ni UPDATE ni rastro. Rastro: Modificaciones Tabla `CompraFechaPorCorreo`; marca en el correo
  (EmpresaAplicada, PedidoAplicado, FechaRecepcionAplicada, AplicadoEl, AplicadoPor). El histórico NUNCA aplica. Anota la promesa en riesgo (#693) si retrasa.
- **Mismo envío** (`MismoEnvio`, puro, sin columna): correos que comparten un código (Codigo + Seguimientos) con < 10 días; se deciden juntos al final
  de la tanda (caso UPS ×3 + MBE con el proveedor). Heredan los candidatos (motivo `MismoEnvio`); uno aplicado fija el pedido de los demás. Remitente del
  paquete (`ExtractorRemitentePaquete`, columna `RemitentePaquete`) y su memoria `ComprasRemitentesPaquete` (motivo `RemitentePaquete`, tras el número).
  **Por el nombre del remitente (NestoAPI#905, caso Postquam/SEUR PV26-57984, 25/11/26)**: si ningún criterio da proveedor, LA REGLA (pura)
  `ReglaNombreRemitentePaquete` (`RemitentePaquete.cs`) compara el remitente con el nombre de los proveedores con pedidos en el aire: palabra
  DISTINTIVA entera (sin forma societaria, tildes ni mayúsculas; ≥ 5 letras; nunca las genéricas, `Genericas`: COSMETIC, DISTRIBUCIONES…) y solo
  si casa UN proveedor (el falso amigo no da ninguno). Motivo `NombreRemitentePaquete`, el último (tras número, memoria, correo, dominio y nombre;
  también sirve para dar por bueno un número suelto). Un pedido suyo → inequívoco; varios → por asignar. Aplicado el envío, el remitente pasa a
  la memoria (`ReglaRecordarRemitentePaquete`, usuario «(automático)»; nunca rompe). Sin script.
  Por asignar: UNA campana `CompraSeguimientoPorAsignar` a Compras por envío (`AvisarPendientes`, 20 min después de su último correo).
- `GET …/PorAsignar?dias=7` (por envío; candidatos con FechaEnvio, LineasPendientes/YaRecibidas y `Sugerido` = el enviado más reciente antes del primer
  correo, `ReglaPedidoSugerido`), `POST …/Correos/{id}/Asignar[?reasignar=true]` {Empresa, Pedido, RecordarRemitente} (también con el interruptor
  apagado; se propaga al envío; guarda `EligioSugerido`), `POST …/Correos/{id}/Descartar` (se propaga), `GET/DELETE …/RemitentesPaquete[/{id}]`,
  `POST …/Correos/Reanalizar?dias=7` (Dirección/Informática, 1-30: relee por fechas, rehace análisis y enlaces de lo no aplicado; no aplica ni avisa).
- **Avisos de salida del proveedor (#857e, Carlos 18/11/26; caso DDUUEETT 220923)**: no hay «correos de proveedor» aparte: el remitente marcado
  (`AgenciasEntradaRemitentes`, `Scripts/Issue857e_ComprasCorreosRemitentesPdf.sql`; sin él, como antes) puede ir SIN agencia (proveedor), con
  `FiltroTexto` («albarán de entrega|delivery note»: asunto o cuerpo, sin mayúsculas ni tildes, palabras enteras; si no casa, el correo es de un
  remitente sin marcar) y `LeerPdf`. Con `LeerPdf` (job y Reanalizar; el histórico NO) se bajan por Graph hasta 3 PDF de 5 MB y se leen EN MEMORIA
  (`ILectorTextoPdf` → PdfPig 0.1.16, 10 páginas; nada a disco ni a BD). `ExtractorAvisoSalida` (puro): pedido = número del texto que es un pedido
  en el aire de SU proveedor (etiquetado «REF/Pedido/PO…» primero; uno solo → inequívoco, motivo `PdfAdjunto`; sin proveedor, solo etiquetado),
  agencia por `AgenciaPorNombreEnTexto` («Compañía de transporte: SCHENKER») y entrega por `ExtractorEntregaEstimada`. Se guarda y aplica por el
  camino de siempre (fase 3, interruptor, 3 días, rastro, promesa en riesgo, reenvío con «Aviso de salida del proveedor: pedido N leído del PDF»).
  Columnas `AvisoProveedor`, `PedidoEnAdjunto`, `AdjuntoLeido` (SinPdf/Leido/SinTexto/Ilegible/Grande/NoDescargado). Certeza: un aviso del
  proveedor SIN código ni enlace (`CorreoAplicadoPedidoCompra.SinSeguimientoAgencia`) no es seguimiento de agencia → Aproximada, no CasiSegura.
- **Boletines y remitentes ignorados (#857e, falso positivo 18/11/26: boletín de comunicaciones@perfectbeauty.es por `track.…/click.php?L=…&J=…`)**:
  el genérico no cuenta los redirectores de clic (`RedirectorMailing`: dominios de envío masivo —sendgrid, list-manage, mailjet, acumbamail,
  brevo, hubspot…—, tramos click.php/click/ls/click/track/click… o parámetros L y J). Un boletín (`BoletinComercial`: «darse de baja»,
  «unsubscribe», «ver en el navegador»… o buzón comunicaciones@/newsletter@/news@/marketing@…) no se guarda por la vía genérica (solo si una agencia
  registrada saca su seguimiento o es de un remitente marcado). Marca `Ignorar` del remitente (columna del mismo Issue857e): sus correos ni se
  guardan, ni se reenvían, ni avisan. `POST …/Remitentes` con `Ignorar`; `POST …/Correos/{id}/Descartar?ignorarRemitente=true` deja la dirección
  exacta como ignorada (rastro `CompraCorreoRemitenteIgnorado` en Modificaciones; respuesta con `RemitenteIgnorado`). Nesto: botón pendiente.
- **Dos niveles (#857f, Carlos 19/11/26)**: NIVEL 1 = el FORMATO del aviso del proveedor (su ERP; `IFormatoAvisoSalida` en `FormatosAvisoSalida.cs`,
  núcleo `FormatoAvisoSalidaBase`; hoy `FormatoAvisoSalidaDduueett` y el genérico de respaldo): agencia, número de envío, pedido y su entrega. Formato
  nuevo = una clase + una línea en `FormatosAvisoSalida.Registrados`. NIVEL 2 = la AGENCIA (`IAgenciaEntrada`): además de reconocer y sacar el código,
  `EsCorreoPropio`, `LeerEntrega` (día, tramo y `EstadoEnvioAgencia` EnCamino/EnReparto/Reprogramada/Entregado de SUS correos; por defecto el extractor
  común; propias SEUR —«Entrega prevista Jueves 19 de Noviembre», «Se entregará HOY de 13:45 a 14:45»— y UPS) y `UrlSeguimiento` (UPS y DB Schenker/DSV).
  TODO lo de cada agencia vive en su clase (Carlos 19/11/26: nada de «si es Schenker…» fuera). Un correo de la propia agencia SIN código (SEUR, enlaces
  opacos) se guarda si dice entrega o estado; SEUR toma como código la referencia del remitente («Tu envío PV26-56989 de Postquam»).
  Precedencia (`ReglaFechaRecepcionPorCorreo`, por bulto): entregado > lo que dice la agencia (correo o consulta, `OrigenEntrega` Agencia/Consulta) >
  el aviso del proveedor (Proveedor; también filas de antes sin origen) > recibido + 24/48 h; nunca antes de hoy. Un correo «Entregado» no aplica
  fecha (desenlace `Entregado`). Consulta (marco, sin ninguna agencia hoy): `IConsultaEntregaAgencia` (se implementa SOLO en la clase de la agencia) +
  job `compras-consulta-entrega-agencias` L-V 7:30 Madrid (`ServicioConsultaEntregaAgencias`: aplicados con pedido en el aire, `ConsultaEntregaSegura`
  8 s y nunca lanza, `Corregir` con `PeticionAplicacionFecha.Corregir`), interruptor `Compras.CorreosSeguimientoConsultarAgencia` (solo «1»; escribir exige
  además el de aplicar). `Scripts/Issue857f_ComprasCorreosEstadoEnvio.sql`: `EstadoEnvio`, `OrigenEntrega`, `EntregaConsultadaEl` (opcionales, mismo trato).
  Reanalizar ahora también INSERTA lo que antes no se guardaba (`Insertados`). La DSV pública da 429: PROHIBIDO intentar leerla.
- **Nacex, MBE, Weelko y Schenker (#857g, Carlos 19/11/26)**: el analizador prueba PRIMERO las agencias que mandan el correo (`EsCorreoPropio`).
  `IAgenciaEntrada` suma `PlazoEnLaborables` y `LeerDatosEnvio` (`DatosEnvioAgencia`: remitente del paquete, referencias del remitente, mensajero
  real, salida nuestra); `SeguimientoCorreo.Agencia` = la agencia de ESE código si no es la del correo (su enlace lo compone ella). Nacex lee
  «Tu entrega está programada para el dd/MM/yyyy antes de las HH:mm» (día + Hasta, EnCamino; los horarios NACEX.shop no cuentan); código = el
  albarán; sin `UrlSeguimiento`. MBE = INTERMEDIARIO: por cada «Envío: N» del resumen, remitente (nombre hasta la forma societaria), «Ref.:»,
  «Courier:» y sus «Tracking:» (marcados con su agencia) + «Tracking MBE:»; los envíos nuestros del resumen no cuentan (todos nuestros = salida).
  Así queda en el mismo envío que los avisos de UPS, que dan la fecha. Weelko (`FormatoAvisoSalidaWeelko`, nivel 1): solo su CONFIRMACIÓN de pedido
  (`IFormatoAvisoSalida.Confirmacion`): no se guarda como correo ni pone fecha; va a `dbo.ComprasConfirmacionesProveedor`
  (`Scripts/Issue857g_ComprasConfirmacionesProveedor.sql`, opcional) con nuestros candidatos (`ReglaPedidoConfirmado`: la referencia, el único o el
  único enviado 48 h antes). Un correo con «Ref.: SO253360…» se asigna a esos candidatos (`ReglaPedidoProveedor`, motivo `PedidoProveedor`; no
  pisa número ni PDF). DB Schenker: 72 h LABORABLES (`PlazoAgenciaEntrada`, festivos de Algete); las demás, horas naturales como antes.
- **Fecha línea a línea por los productos del aviso (#857h, Carlos 20/11/26; caso Weelko WK/OUT/78316 → 220931)**: `IFormatoAvisoSalida.Lineas` (vacía en la
  base; Weelko lee su «Resumen del pedido»: «[REF] descripción … 2,00», el transporte sin corchetes no cuenta). LA REGLA (pura) `ReglaLineasAvisoSalida.Casar`
  (`LineasAvisoSalida.cs`): referencia → producto por ProveedoresProducto del proveedor de cada pedido; por producto, las unidades se reparten por nº de orden
  (como mucho lo pendiente); menos que lo pendiente = PARCIAL: la fecha se pone igual (no se parte la línea) y lo dice el rastro. Un pedido casa → inequívoco
  (motivo `LineasAviso`: solo nuestro número y el PDF mandan sobre él; va por delante del dominio, el nombre y las confirmaciones); varios → gana el ÚNICO que casa el aviso entero (todas sus líneas y unidades) y, si no lo hay o hay dos, por asignar con
  los dos; ninguno → como antes. Lectura: `LeerLineasAviso` (una consulta por tanda, solo si el aviso trae líneas; ni el histórico). Al aplicar (job, a mano,
  mismo envío —los correos de la agencia heredan las líneas, `LineasAvisoCorreo.DelEnvio`—, Corregir) se vuelve a casar con ESE pedido
  (`LeerLineasAvisoDelPedido`) y `PeticionAplicacionFecha.Ordenes` limita el UPDATE, la promesa en riesgo y el rastro (`LineasAviso`, `EnvioParcial`); sin
  casar ninguna, todo el pedido. Columnas `LineasAviso`/`LineasAplicadas` (`Scripts/Issue857h_ComprasCorreosLineasAplicadas.sql`; sin ellas, la fecha a todo
  el pedido). Certeza (#853/#869): un correo con `LineasAplicadas` solo da seguimiento a las líneas de esos PRODUCTOS (`ReglaSeguimientoAgencia.Vigente(…, producto)`).
  GET …/Correos y …/PorAsignar: `LineasAviso`, `LineasCasadas`, `LineasAplicadas`. Reenvío: «Fecha de recepción del 26/11 al 24/11 en 2 líneas del pedido…: 32831 (2 uds.) y 40695 (1 ud.).»
- **Control de pendientes (NestoAPI#897, regla de Carlos 25/11/26; caso Maystar 220093/220983/220993, DHL 4313144450)**: en un proveedor con
  `Proveedores.ControlPendientes` (en `PedidoCompraEnAire`, con `FechaPrimerEnvio` de `PedidosCompraEnvios` si está la tabla) y varios candidatos de
  ÉL, sin nuestro número, PDF, líneas del aviso ni confirmación, LA REGLA (pura) `ReglaControlPendientes` (`ControlPendientesCorreo.cs`): «de pendientes»
  = alguna línea ya recibida o enviado (primer envío o [Fecha Modificación]) más de N días antes del correo (ParametrosUsuario «(defecto)»
  `Compras.DiasPedidoDePendientes`, 30 sin fila; se lee solo si hay algo que decidir y nunca crea fila): nunca recibe la fecha por esta regla. De los
  demás enviados ANTES del correo: uno → ese; varios → el MÁS ANTIGUO (el otro puede venir agrupado: no se toca); ninguno → por asignar. Un solo
  candidato con motivo `ControlPendientes` y `Explicacion` en el JSON de candidatos («MAYSTAR, S.L. tiene control de pendientes: se asigna al pedido
  en curso más antiguo, 220983; el 220093 es de pendientes y el 220993 puede venir en el mismo envío.»), que va al reenvío y al rastro (`PorQuePedido`).
  Job y Reanalizar (el histórico no). `GET …/PorAsignar`: cada candidato con `DePendientes` y `MotivoDePendientes`. Bultos y kilos: los lee SOLO la
  clase de la agencia (`IBultosKilosAgencia`, hoy DHL: «Bultos.....: 8», «Kilos.......: 123»), al vuelo del `Extracto`: `Bultos`/`Kilos` en GET
  …/Correos y …/PorAsignar y «Bultos: 8 bultos, 123 kg» en el reenvío; nunca deciden. Sin script.
- **Delegación de origen de la agencia, aprendida (NestoAPI#857i, Carlos 25/11/26; DB Schenker ESVIC262013788, sin remitente, pedido ni referencia)**:
  la AGENCIA dice el origen (`IAgenciaEntrada.OrigenDelEnvio`/`NombreOrigen`, null por defecto; hoy SOLO `AgenciaEntradaSchenker`: ES + 3 letras + 9
  cifras, mapa `Delegaciones` VIC Vic, MNR Manresa; nada de «si es Schenker» fuera). Memoria `dbo.ComprasOrigenesAgencia` (Agencia, Origen, Empresa,
  Proveedor, Veces, UltimaVez, Usuario; `Scripts/Issue857i_ComprasOrigenesAgencia.sql`; sin ella, como antes, caché 10 min y ELMAH una vez). Se aprende
  (suma Veces) cuando el envío queda aplicado por una PERSONA (`Asignar`, `OrigenAgenciaAprendido` en la respuesta) o, en el job, por un criterio fuerte
  (`ReglaOrigenAgencia.MotivosQueEnsenan`: número, PDF, líneas del aviso); NUNCA de su propia regla ni del control de pendientes; descartar no toca nada.
  LA REGLA (pura) `ReglaOrigenAgencia` (`OrigenAgencia.cs`), solo si nada ha dado candidatos (tras el nombre del remitente, antes del control de
  pendientes), job y Reanalizar: recordados de esa agencia + origen en 12 meses; pedidos en el aire enviados ANTES del correo. Uno con uno → ese
  (motivo `OrigenAgencia`, `Explicacion` «DB Schenker desde Vic: las otras veces era de X; se asigna a su único pedido en el aire, 220989.»); uno con
  varios → por asignar con los suyos (decide #897 si tiene control de pendientes); varios recordados → nunca por frecuencia: solo uno con UN pedido
  → ese («…; B no tiene nada pendiente: …»); si no, por asignar con los de ellos (motivo `OrigenAgenciaVarios`, que no deja decidir a #897). `OrigenAgencia`
  («Vic» o la clave) y `Explicacion` en GET …/Correos y …/PorAsignar. `GET/DELETE …/OrigenesAgencia[/{id}]` (permisos de RemitentesPaquete; rastro
  `ComprasOrigenesAgencia`). Lo que el proveedor manda directo a un cliente no se sabe sin la web (prohibida): no se intenta.
- **Retoques del 25/11/26**: (1) DHL saca el remitente del paquete de «¡Muy pronto recibirás el envío de MAYSTAR S,L !» y su «Ref.Cliente.: 128013»
  (en `AgenciaEntradaDhl.LeerDatosEnvio`) va a `DatosEnvioAgencia.NumerosQueNoSonPedido`: el asignador no lo cuenta como número de pedido nuestro.
  (2) `ReglaRecordarRemitentePaquete`: al aplicarse un envío se recuerda el remitente del paquete de un correo cuyos candidatos PROPIOS llevan ese
  proveedor por cualquier criterio (no solo el nombre del remitente; nunca lo heredado), si aún no está en la memoria. (3) Reenvío por asignar: «Puede
  asignarlo ya en Nesto: Productos › Envíos por asignar.». La campana espera 20 min desde que se RECIBIÓ el último correo (ya era así); si no han
  pasado, `ProximoAvisoPendiente` y el job programa en Hangfire un aviso suelto (`CorreosSeguimientoComprasJobsService.AvisosPorAsignar`) si toca
  antes de la siguiente pasada. (4) LA REGLA (pura) `ReglaEnvioYaNoPendiente` (`MismoEnvio.cs`), por envío: aplicado o descartado algún correo, la
  agencia dice Entregado, ya tiene `PedidoReal`, o tiene candidatos (propios o heredados) y ninguno sigue en el aire → no sale en `GET …/PorAsignar`
  (salvo `?incluirLlegados=true`, con `MotivoYaNoPendiente`) ni avisa la campana. Caso DSV ESMNR264773770: los correos de estado que llegan DESPUÉS de
  aplicar el envío no se marcan aplicados (el job sale antes por «ya llegó»/«entregado»); lo resuelve la regla de listado.

### Fechas de recepción y certeza (NestoAPI#853, 12/11/26)
- Plazo real por proveedor en `dbo.ProveedoresPlazosEntrega` (fuera del EDMX, `Scripts/Issue853_PlazosProveedoresYEnvios.sql`): mediana y P80
  de días (CabPedidoCmp.[Fecha Modificación] → FechaAlbarán, líneas TipoLínea '1', 24 meses, empresas 1+3, negativos fuera), cadencia de
  pedidos del último año y fila genérica '*'. Cálculo puro `CalculoPlazosProveedores`; job `proveedores-plazos-entrega` 3:40 Madrid (cola
  «indicadores»); `GET api/Compras/PlazosProveedores` (Compras/Dirección/Informática), `POST …/Recalcular` (Dirección/Informática, 202).
  Caché de 10 min (`CachePlazosProveedores`). Proveedor habitual de un producto = ProveedoresProducto con menor Orden; si no, el de su última compra.
- Al ENVIAR un pedido de compra (PUT api/PedidosCompra con PathPedido): con ParametrosUsuario «(defecto)» `Compras.FechaRecepcionPorPlazoReal`
  = «1» y plazos, `ReglaFechaRecepcionEstimada` (hoy + P80 propio con ≥5 recepciones, nunca menos que DíasEnServir; si no, DíasEnServir; si
  no, P80 genérico); si no, hoy + DíasEnServir. Nunca rompe el envío. Siempre (con tabla) se apunta en `dbo.PedidosCompraEnvios` (primer envío;
  `FechaModelo` reservada para ML.NET) y `dbo.vPedidosCompraEnviosError` mide el error.
- Certeza en el motor #606 (aditivo en `PartidaFechaEntregaAgenciaDTO`): `Certeza` (Segura, CasiSegura, Aproximada, Orientativa),
  `ConSeguimientoAgencia`/`AgenciaSeguimiento` (correo de #857 aplicado, no descartado, con FechaRecepcionAplicada = la FechaRecepción de
  ahora), `FechaOrientativa` (solo sin fecha: NO entra en ninguna fecha, prometida, promesa en riesgo ni simulación), `PedidoProveedorOrientativo`
  y `FechaPrevistaProveedorOrientativo` (pedido enviado con prevista a >15 días: la regla de los 15 días NO cambia). Orientativa de lo no pedido:
  `ReglaFechaOrientativaSinPedir` (hoy + hasta el próximo pedido según cadencia + P80). Lectura por tanda en `ILectorCertezaFechaEntrega`.
- Textos: UNA pieza pura `TextosCertezaFechaEntrega` para el motivo del motor, el correo (etiqueta «[Prácticamente segura]», «[Aproximada]»,
  «[Orientativa]» al principio de cada bloque) y la promesa en riesgo. Fotos: proveedor con seguimiento → caso `EnTransito`.
- **Modelo ML.NET en sombra** (`PlazosEntrega/ModeloPlazos/`): LightGBM de regresión de log(1 + días hasta el PRIMER albarán), 5 años, un
  ejemplo por pedido recibido (0-120 días). Rasgos puros sin fuga (`ConstructorRasgosPlazos`: solo pedidos enviados ANTES de ese día y plazos
  con albarán ANTES): proveedor (<10 pedidos → «(otros)»), día/mes, líneas, log importe, DíasEnServir, mediana/P80/último de sus 10 últimos
  plazos, días desde su pedido anterior y pedidos en el aire. Prudente = media en log + P80 de los residuos (no hay cuantil público en 4.0.2).
  Job `plazos-proveedores-modelo` 4:10 Madrid (cola «entrenamiento»): valida con los últimos 3 meses (margen calibrado con los 3 anteriores) contra
  la regla «como si» (`ReglaPlazosComoSi`), apunta SIEMPRE las métricas en `dbo.PlazosProveedoresModeloEntrenamientos` y solo cambia el modelo si
  no empeora (`PuertaCalidadPlazos`: % corto +1 punto, error +0,25 días). Modelo en `ModeloLlamadas:Carpeta` (`plazos_proveedores.zip`/`.json`
  con la historia reciente por proveedor). Al enviar rellena `FechaModelo` (3 s como mucho; si falla, NULL). ParametrosUsuario «(defecto)»
  `Compras.FechaRecepcionQuienManda` = «Regla» | «Modelo» (con el interruptor de plazo real a «1»: la del modelo, nunca < hoy + DíasEnServir;
  Origen `Modelo`/`ModeloDiasEnServir`). `GET api/Compras/PlazosProveedores/Comparacion?meses=6` (último entrenamiento + lo real con veredicto,
  ≥50 recibidos) y `POST …/Modelo/Entrenar[?ahora=true]` (Dirección/Informática; L-V 8-20 → a las 21:00).
- `AsignadorPedidosCompra` (#857): un número cuenta como pedido nuestro solo si es token propio (no pegado a letras/guiones) Y lleva etiqueta
  de pedido/referencia delante (30 caracteres) o su proveedor se reconoce por otro criterio.
- **Medir las orientativas de lo no pedido (NestoAPI#861, 13/11/26)**: la regla no cambia. `dbo.FechasOrientativasSinPedir` (fuera del EDMX,
  `Scripts/Issue861_FechasOrientativasSinPedir.sql`, requiere el de #853): una fila por día + empresa + producto con venta pendiente cuya partida del
  motor queda SIN fecha y con orientativa de lo no pedido (`ReglasFechasOrientativas.EsOrientativaSinPedir`: SinStock, sin pedido lejano) y sus
  ingredientes (proveedor habitual, último pedido, cadencia, días desde/hasta, plazo propio/genérico, llegada de la regla y la salida que ve el usuario,
  `YaTocabaPedir`, `PrimeraDelEpisodio` = sin foto el laborable anterior). Job `fechas-orientativas-sin-pedir` 3:50 Madrid (cola «indicadores»): FOTO
  con el mismo motor que `fechas-entrega-fotos-sombra` (sin avisos, nunca en caliente) y RESOLVER por lotes de Id con una consulta: primer pedido
  al MISMO proveedor (empresas 1+3) con el producto enviado desde el día de la foto (`PedidosCompraEnvios.FechaEnvio` o `[Fecha Modificación]`) y
  su primer albarán; errores real − previsto; sin llegar a los 120 días, `Caducada`. Sin tabla, nada (ELMAH una vez). `dbo.vFechasOrientativasError`
  (primeras resueltas por proveedor; Grupo Todas/YaTocaba/NoTocaba). `GET api/Compras/PlazosProveedores/Orientativas?meses=6` (Compras/Dirección/
  Informática; lo mismo en C# con los meses y los totales) y `POST …/Orientativas/Procesar` (Dirección/Informática, 202); 503 sin el script.
- **Packs montables (NestoAPI#606, 18/11/26, caso 44376)**: un producto TITULAR de un kit (tabla Kits, sin materias primas MTP) sin stock propio
  suficiente en Algete se MONTA con sus componentes (origen `Montaje`, después de libres y de desmontar kits y antes que las tiendas). Lo monta almacén a
  mano (`POST api/Productos/MontarKit`, Diario _MontarKit) con el informe de kits de antes del picking (`prdInformeKitsQueSePuedenMontar`, parte «m»): el
  picking NO monta (sin stock del pack no lo reserva). Mismo supuesto que el origen `Kit` (desmontar). LA REGLA (pura, `BolsaMontajeAlgete` en
  `MontajeKitsAlgete.cs`): componente libre = stock Algete − pendientes de otros en Algete − lo que pide suelto el propio pedido − lo que necesitan los packs
  que otros pedidos deben por encima del stock de ese pack; se montan los que permite el componente más escaso → Segura, sale como lo libre. Lo que no se
  puede montar: cada componente que falta se reparte con las reglas de cualquier producto (`ComponentesMontaje`, un solo nivel, sin lo libre de Algete) y el
  pack sale el día del PEOR (`Montaje` con `ComponenteQueFalta`/`NombreComponente`/`OrigenComponente` y la certeza de ese componente) o, si alguno no tiene
  fecha, `SinStock` nombrándolo («para montarlo en Algete falta el componente 40010 …»; su orientativa no se mide en #861). Lectura por tanda:
  `SQL_KITS_MONTAJE` (una consulta) y los componentes entran en la lectura general; `SQL_ESTADOS` trae el nombre. Llega a todo lo que usa el motor
  (prometida, correo, promesa en riesgo, fotos: un pack que se monta ya «sale del stock»; el que espera, se mide como su componente). El modo de servicio
  sugerido (`SugeridorModoServicio`) no lo usa aún; el aviso de Nesto antes del picking solo pregunta por los kits que DESMONTAR.

### Fechas de recepción vencidas: se recalculan (NestoAPI#865, 16/11/26)
- En el job del correo de proveedores vencidos (L-V 8:00, `ProveedoresVencidosJobsService`), ANTES del correo, `ServicioFechasVencidas`
  (`Infraestructure/PedidosCompra/FechasVencidas.cs`) pone fecha nueva a cada línea en el aire (LinPedidoCmp -1/1, Enviado) con FechaRecepción < hoy.
  Regla pura `ReglaFechaVencida`: correo de seguimiento #857 del pedido (envío aplicado, o inequívoco sin descartar ni llegado:
  `CorreosDelPedidoCompra`) con `ReglaFechaRecepcionPorCorreo.DiaDeLlegada` ≥ hoy → ese día; si no («sin noticias»), de las recepciones del
  proveedor (mismo origen que #853, 24 meses, 1+3, `LeerRecepcionesDeProveedores`) que tardaron MÁS de los días que lleva esperando
  (desde CabPedidoCmp.[Fecha Modificación]), el P80 desde ese día; con < 5 casos, hoy + P80 genérico ('*'); sin nada, no se toca. Lo
  estimado nunca antes del siguiente laborable de Algete.
- Escritura (`GuardarFechaRecalculada`, una transacción por línea, solo si sigue en el aire y con la fecha leída): Usuario «Fecha recalculada
  (vencida)», [Fecha Modificación], Modificaciones Tabla `CompraFechaRecalculada` (JSON con Clave «empresa/pedido/orden», antes/después, origen y
  cifras) y promesa en riesgo (#693) por el núcleo común `CambiosCompraPromesaRiesgo.AnotarCambioFecha` (el mismo de la fase 3 de #857).
  Se recalcula cada vez; desde el 2.º vencimiento (filas del rastro de esa línea en 365 días) el correo marca «Hay que llamar al proveedor».
  Correo: columna «Nueva fecha» con `TextosFechaVencida` (de usted). Interruptor ParametrosUsuario «(defecto)» `Compras.RecalcularFechasVencidas`
  (sin fila o «1» encendido; «0»/false/no/off/apagado → el correo de siempre). Script `Scripts/Issue865_ComprasFechasVencidasYReenvio.sql`.
- **La agencia ya lo tiene en camino (caso 220983, Carlos 25/11/26)**: una línea NO está «sin noticias» si su pedido tiene un envío de seguimiento
  VIVO (`CorreosDelPedidoCompra.Envios`, pura: aplicado a ese pedido, o sin aplicar ni descartar con él entre sus candidatos —en un proveedor con
  control de pendientes, solo si no es «de pendientes», #897—; último correo de los últimos `ReglaFechaVencida.DIAS_CORREO_VIVO` (7) días; sin
  llegar). Entonces, si un correo vivo trae entrega estimada futura, esa (origen `CorreoSeguimiento`); si no, origen `AgenciaEnCamino`: hoy si el job
  corre en laborable antes de la hora de corte del picking, si no el siguiente laborable, «DHL lo tiene en camino (expedición 4313144450, aviso del
  24/11): debería llegar hoy.»; NO cuenta para «hay que llamar» (ni esa vez ni en el rastro). Con un aviso de más de 7 días sin llegar, la estadística
  de siempre y el texto empieza por «DHL avisó el 14/11 (expedición …) y no ha llegado; ». Los pedidos en el aire solo se leen si hay un envío por
  asignar con varios candidatos. Sin script.
- Corte 2: en «Clientes esperando», lo que trae una reposición de tienda en camino (PreExtrProducto con NºTraspaso, neto por traspaso y
  producto > 0, en el almacén de destino: lo mismo que el «en camino» del motor) sale aparte con su llegada (siguiente día de reparto
  propio, `GestorFestivos.EsFestivoRepartoPropio`) y NO cuenta como falta (`EsperaClientesProducto.CubiertoPorReposicion`; `Faltan` lo descuenta).
- Corte 3 (TEMPORAL): el job `compras-correos-seguimiento` reenvía a compras@ cada correo que acaba de GUARDAR (`ReenviadorCorreosSeguimiento`,
  `CorreosSeguimiento/ReenvioCorreosSeguimiento.cs`) por el SMTP de siempre: asunto «[Nesto: correo de seguimiento] …», cabecera con lo hecho
  (`AlGuardar` devuelve el desenlace por MensajeId: fecha antes → después y pedido, misma fecha, por asignar, salida nuestra, descartado, ya
  recibido, aplicar apagado, antiguo…; textos `TextosReenvioCorreoSeguimiento`) y debajo el cuerpo que Graph dio en esa pasada (no se guarda;
  Graph sigue solo lectura). Idempotente sin columna (solo lo recién insertado; el INSERT es único por MensajeId). El histórico y Reanalizar no
  reenvían. El analizador ignora los correos con esa marca en el asunto (por si vuelven al buzón). Fallo → ELMAH una vez al día, nunca rompe
  la pasada. Interruptor `Compras.CorreosSeguimientoReenviar`: SOLO «1» = encendido (sin fila, apagado; el script lo crea a «1»).

### Próxima entrada en la ficha del producto (NestoAPI#869, 16/11/26)
- `GET api/Productos/ProximaEntrada?producto=&almacen=ALG&empresa=1` (`ProximaEntradaProductoController`, solo lectura, `[Authorize]`; 404 si no
  existe): (1) la línea en el aire (LinPedidoCmp -1/1 Enviado, 1+3) de FechaRecepción más temprana, Aproximada o CasiSegura por `ReglaSeguimientoAgencia`
  (correo #857 vigente), con `Recalculada` (Usuario de #865) y `PrevistaPasada`; (2) sin pedido, Orientativa = `ReglaFechaOrientativaSinPedir` del
  proveedor habitual + `FechaSiSePidieraHoy` (`ReglaFechaRecepcionEstimada` sin DíasEnServir); (3) `SinDatos` con `Motivo`. `Texto` hecho
  (`TextosProximaEntrada`, calificativos de `TextosCertezaFechaEntrega`). Lee con `LectorCertezaFechaEntregaSql`. Aparte de GET api/Productos (que va por Nesto_sync).

## Correos de agencia sin reconocer (NestoAPI#857j, Carlos 26/11/26; caso SEUR de Iberocel «Identificador de envío: A-2609942»)
- Antes, un correo de la PROPIA agencia (SEUR, UPS, MBE…: la clase dice `EsCorreoPropio`) al que su clase no sacaba ni código ni entrega ni
  estado no se guardaba ni dejaba rastro: un formato nuevo solo se descubría viéndolo en el buzón. Ahora el analizador lo marca
  (`AnalisisCorreoSeguimiento.AgenciaSinReconocer`; `Guardar` sigue false) y el JOB (solo él: ni el histórico ni nada más) lo apunta APARTE,
  en `dbo.ComprasCorreosSinReconocer` (`Scripts/Issue857j_ComprasCorreosSinReconocer.sql`): MensajeId (único, como los demás), Recibido,
  Remitente, Agencia, Asunto, Extracto (los primeros 2.000 caracteres del texto), FechaAlta, ReconocidoEl, CorreoId. Tabla aparte a propósito:
  no sale en «por asignar», no propone proveedor ni pedido, no toca fechas, no se reenvía ni avisa. `ResultadoPasadaCorreosSeguimiento.SinReconocer`.
- LA REGLA (pura) `ReglaCorreoAgenciaSinReconocer` (`CorreosSinReconocer.cs`): solo si no se guarda, no es una confirmación de pedido ni de un
  remitente ignorado; agencia = la primera cuya clase dice que el correo es suyo. NO se apuntan (`MotivoDescarteAgencia`): su publicidad
  («Boletín», el mismo `BoletinComercial` que ya la descartaba; `Seur_PublicidadDeLaAgencia_NoSeGuarda` sigue igual) ni sus encuestas
  («Encuesta»: «tu opinión», «¿qué tal fue…?», «survey»…).
- `GET api/Compras/AgenciasEntrada/Correos/SinReconocer?dias=30` (permisos de esa ruta: Compras, Dirección, Informática; 1..365 días) → los NO
  reconocidos, lo más reciente primero [{Id, MensajeId, Recibido, Remitente, Agencia, Asunto, Extracto, FechaAlta, ReconocidoEl, CorreoId}]; 503
  sin el script. Sin la tabla el job sigue igual (ELMAH informativo una vez; caché de 10 min, sin reiniciar). Apuntar nunca rompe la pasada.
- `Reanalizar` (que relee el buzón por fechas): un apuntado que ahora SÍ se reconoce se guarda como correo normal (lo de siempre, `Insertados`)
  y aquí queda con ReconocidoEl y su CorreoId (`SinReconocerReconocidos`); uno de agencia que sigue sin reconocerse y no estaba, se apunta
  (`SinReconocerApuntados`). Flujo para un formato nuevo: se ve en la lista, se enseña a la clase de su agencia, se publica y se reanaliza.
- Nesto (junto a «Envíos por asignar»): una pestaña o sección «Correos de agencia sin reconocer» que llame al GET (Fecha, Agencia, Remitente,
  Asunto y el Extracto al desplegar); con 503, ocultarla. Solo lectura: no hay que asignar ni descartar (desaparecen al reconocerse).
