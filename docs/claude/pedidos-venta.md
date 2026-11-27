# Pedidos de venta

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Guardar, validar, autorizar, modos de servicio, picking y faltas, pago a plazos.
Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.

### Nota de entrega (solo cabecera) y borrar un pedido
- **Nota de entrega: cambios solo de cabecera (novedad 1365, 19/11/26)**: el PUT de una nota ya no da «No se puede ampliar una nota de entrega»
  si solo toca la cabecera («Recoger producto», aviso con importe, comentarios…): regla pura `PedidosVentaController.EsCambioSoloCabeceraEnNotaEntrega`
  (ni cliente, contacto, IVA ni DescuentoPP; ni líneas nuevas o que faltan; ni producto, texto, precio, cantidad, descuentos o fecha; fecha default y
  almacén vacío no cuentan). También en una nota ya procesada (líneas -2) sin nada facturado (`PuedeModificarsePedido`).
- **Borrar un pedido (Novedades 1283, Carlos 20/11/26: «nada puede desaparecer mientras haya mercancía o dinero en marcha»; sin excepciones por grupo)**:
  `DELETE api/PedidosVenta` comprueba ANTES de tocar nada LA REGLA (pura) `ReglaBorrarPedidoVenta.MotivoNoSePuedeBorrar`: líneas con picking, en albarán o
  factura o procesadas (-2), envíos en curso o más (las etiquetas pendientes se siguen borrando), prepagos (suma ≠ 0) o lo cobrado con enlaces (`LectorEnlacesPagoPedido`).
  409 `PEDIDO_NO_SE_PUEDE_BORRAR` con todos los motivos en una frase y qué hacer; si no se puede leer (`LectorBorrarPedidoVentaSql`, una consulta + enlaces), 503 y nada
  borrado. Va ANTES del 409 de las inscripciones (#732). Los borrados internos (anular recogida #855, traspaso entre empresas) no pasan por aquí.

### Pedir autorización de un pedido que no pasa la validación (Novedades 1258, 20/11/26; Nesto gemelo)
- LA REGLA de quién puede «crearlo de todos modos» = quién puede AUTORIZAR el de otro: `ReglaOmitirValidacionPedido` (`Infraestructure/PedidosVenta/
  Autorizaciones/`), sacada tal cual del POST de PedidosVenta (que ya la usa): Dirección, Almacén o ParametrosUsuario `PermitirCrearPedidoConErroresValidacion`;
  Tiendas si TODAS las líneas son de su `AlmacénPedidoVta`; y el `PermisoNecesario` de la denegación (#501) si lo pide. Los parámetros se leen solo si hace
  falta (la fila «(defecto)» crea la del usuario). El PUT y `UsuarioPuedeOmitirValidacion` siguen con su copia de siempre (no se han tocado).
- `dbo.PedidosAutorizacionesSolicitadas` (fuera del EDMX, `Scripts/PedidosAutorizacionesSolicitadas.sql`; sin ella los seis endpoints dan 503): PedidoJson
  (el DTO tal como se iba a guardar; `RecogeEnTienda` se guarda como lo mandó el cliente, no calculado), Motivos (JSON {Motivos, Errores}), Estado
  Pendiente/Autorizada/Denegada/Anulada (CHECK) y, además del contrato, Huella, PermisoNecesario, Importe, NumeroLineas, Vendedor y Almacenes.
- `PedidosVentaAutorizacionesController`: `POST api/PedidosVenta/Autorizaciones` {Pedido, Comentario?} (la API VUELVE a validar con `GestorPrecios.EsPedidoValido`:
  si pasa, 400 «no necesita autorización»; Pendiente idéntica del mismo solicitante —`ReglasAutorizacionPedido.Huella`: cliente, contacto y líneas sin
  orden— en 24 h → 409 {Message, Codigo YA_SOLICITADA, Id}) → 201 {Id, Estado, Motivos, Errores, Destinatarios}; `GET …?pendientes=true&mias=false` (30 días;
  ve las suyas y las que LA REGLA le deja autorizar), `GET …/{id}` (+ Pedido; 403), `POST …/{id}/Autorizar` {Comentario?}, `POST …/{id}/Denegar` {Motivo 1..500},
  `DELETE …/{id}` (solo quien la pidió). 409 si ya no está Pendiente.
- **`PuedeAutorizar` y `PuedeAnular` (NestoAPI#909, para NestoApp#294, 26/11/26)**: aditivos en cada `AutorizacionPedidoDTO` de los dos GET, para QUIEN
  PREGUNTA (`ServicioAutorizacionesPedido.ConPermisos`): `PuedeAutorizar` = Pendiente + no es suya + LA REGLA de Autorizar/Denegar
  (`PuedeAutorizar` → `ReglaOmitirValidacionPedido.PuedeOmitir`, con Almacenes y PermisoNecesario guardados en la fila); `PuedeAnular` = suya y Pendiente.
  Los parámetros del usuario se leen una vez por petición (lector memorizado), nunca por fila; en las suyas o las resueltas ni se miran.
- Autorizar: bloqueo `sp_getapplock` de sesión `PedidoAutorizacion:{id}` (otro a la vez → 409), y el pedido se crea por `PedidosVentaController.PostPedidoVenta`
  (el MISMO camino, con el principal de quien autoriza) con Usuario = solicitante y `CreadoSinPasarValidacion`; `PedidosVentaController.UsuarioPermisosValidacion`
  = quien autoriza (sus parámetros, no los del solicitante). Si el alta falla, su respuesta tal cual y la solicitud sigue Pendiente. Rastro en Modificaciones
  (Tabla `PedidoAutorizado`).
- Avisos (`AvisosAutorizacionPedido`, nunca rompen): campana de Nesto NO urgente `AutorizacionPedidoPedida` {tipo, id, referencia, empresa, cliente, contacto} a los
  que pueden autorizar (`ReglasAutorizacionPedido.Destinatarios`: grupos del dominio + filas PROPIAS de ParametrosUsuario, sin «(defecto)» ni el solicitante);
  al resolver, `AutorizacionPedidoResuelta` {…, estado, pedido} al solicitante (campana) y push de NestoApp al vendedor del pedido, y `notificacionResuelta`
  (Tipo `AutorizacionPedidoPedida`, Referencia = Id) a los demás; al anular, solo `notificacionResuelta`.
  Push de NestoApp también a quien la pidió (Carlos 26/11/26) si tiene dispositivo de NestoApp y no es el vendedor del pedido (LA REGLA
  `ReglaPushAutorizacionResuelta.PushAlSolicitante`: ninguno de sus dispositivos es del vendedor; si alguno lo es, es la misma persona y no se duplica). La campana no cambia.

### Recoge en tienda (NestoAPI#571, cortes 1 y 2, 11/11/26)
- Sin columna: la ruta es la verdad. `TiendasRecogida.EsRutaDeRecogida` (AM, ALC, REI). `PedidoVentaDTO.RecogeEnTienda` (bool?): al leer,
  siempre calculado de la ruta; al escribir, null = no tocar (Nesto y apps viejas), y el POST/PUT lo traducen a ruta con las reglas puras
  de `Infraestructure/PedidosVenta/RecogeEnTienda.cs` (ALG→AM, ALC→ALC, REI→REI; false en ALC/REI → 400; el PUT compara con la ruta
  GUARDADA; bloqueo = `CambioAlmacenPedido.Motivo`; al desmarcar vuelve la ruta de la ficha, `RutaQueVale.Leer`).
- `GestorPortes.CalcularPortes`: ruta de recogida (o `PedidoPortesInput.RecogeEnTienda` = true, la plantilla de NestoApp) → ni portes NI
  comisión contra reembolso. Las demás rutas sin portes siguen llevando comisión. Pendientes: picking de recogidas de ALG e impresión en AM.

### Versión del pedido y 409 al guardar con copia vieja (NestoAPI#873, gemela Nesto#642, 17/11/26)
- Caso 929555: un PUT con una copia de antes del prepago («Prepagos: []») lo borró. `PedidoVentaDTO.Version` (opaca): LA REGLA pura
  `VersionPedidoVenta.Calcular` sobre UNA consulta (`LectorVersionPedidoSql`, sin NOLOCK): RowVersion de la cabecera, nº y MAX(RowVersion)
  de las líneas, y nº/suma de Id/CHECKSUM de prepagos y efectos (cambia con cualquier guardado, lo haga quien lo haga). La rellenan
  GET pedido (leída ANTES que el pedido: de más, nunca de menos), POST y la respuesta del PUT (`RespuestaModificacionPedidoDTO.Version`,
  ahora casi siempre 200). `GET api/PedidosVenta/{empresa}/{numero}/Version` → { Version, Usuario, Fecha } (sin leer el pedido).
- PUT con Version distinta → 409 `PEDIDO_CAMBIADO` { error: { code, message, details: { Version, Usuario, Fecha } }, Message } ANTES de tocar
  nada («El pedido 929555 lo ha modificado Aida a las 15:14 desde que usted lo abrió. Recárguelo y vuelva a hacer los cambios.»; quién y
  cuándo = lo más reciente de cabecera/líneas/prepagos/efectos). Sin Version (NestoApp, TNV, Nesto viejo, CambiarCliente, UnirPedidos), como antes.
- Prepagos (`AplicarPrepagosAlModificar`): los que no trae el PUT SOLO se quitan con la Version comprobada (`PuedeQuitarPrepagosQueNoVienen`);
  sin ella se conservan, aviso `PrepagoConservado` (respuesta y correo) y ELMAH. Añadir/actualizar por concepto, como siempre.
- `?devolverPedido=true` (opcional, por tamaño) en PUT y POST: la respuesta trae además el pedido tal como ha quedado (el del GET, con
  los Nº de orden de las líneas nuevas) y su Version, leídos de forma coherente (`LeerPedidoConVersionCoherente`: versión, pedido,
  versión; si cambia mientras, repite 3 veces; si no, null). PUT → `RespuestaModificacionPedidoDTO.Pedido`; POST → `PedidoGuardado` del
  cuerpo. Nulos no viajan. Nesto lo adopta como foto para «Recargar conservando mis cambios» (Nesto#642); NestoApp#285.
- SignalR: `pedidoCambiado` {Empresa, Pedido, Usuario, Fecha, Version} al grupo `Pedido:{empresa}:{numero}` del **AlmacenHub** (donde Nesto ya
  entra/sale con Entrar/Salir), a todos (Nesto ignora el suyo por la Version), solo si alguien lo tiene abierto, en segundo plano
  (`AvisoPedidoCambiado`, nunca lanza): PUT, POST, cobro de la app (`ServicioPagos`) y pago a plazos (`RepositorioPagoAPlazos`).
- **Lo que ha cambiado es el picking (Carlos 26/11/26)**: sacar el picking cambia la RowVersion de las líneas (y la Version) pero no Usuario ni
  Fecha, y Nesto decía «Carlos ha modificado este pedido a las 10:58» cuando fue Almacén. Aditivos, en la MISMA consulta (`LectorVersionPedidoSql`):
  `Pickings` (números ≠ 0 de las líneas, ordenados, sin repetir; FOR XML PATH, sin STRING_AGG) y `HuellaContenido` (`VersionPedidoVenta.HuellaContenido`,
  32 hex): cabecera sin nada del picking ni quién/cuándo (cliente, contacto, fecha, forma y plazos de pago, primer vencimiento, IVA, vendedor,
  comentarios, comentario de picking, avisar con importe, periodo, ruta, CCC, contacto de cobro, nota de entrega, mantener/servir junto, modo de
  servicio, su pedido, no cobrar comisión), líneas por Nº de orden (tipo, producto, cantidad, precio, descuentos, aplicar dto, texto, fecha de
  entrega, almacén, IVA y estado solo si ya está servida: -1→1 no cuenta), prepagos y efectos. Van en `GET …/Version` ({Version, Usuario, Fecha,
  Pickings, HuellaContenido}), en `details` del 409 `PEDIDO_CAMBIADO` y en `pedidoCambiado` (más `Motivo`). La regla del 409 NO cambia (Version).
  Punto único del picking: `GestorPicking` (al terminar de asignar, cada pedido que coge picking; `Usuario` = quien llama a api/Picking, sin él
  «Almacén») y `ServicioQuitarPicking` (Quitado) → `AvisoPedidoCambiado.PorPicking` con `Motivo` «PickingSacado» / «PickingQuitado»: solo si alguien
  tiene el pedido abierto (sin leer nada si no), en segundo plano, nunca lanza. Nesto: si llega una Version nueva y `HuellaContenido` es la que
  tenía, no es una modificación: con `Motivo` «PickingSacado», «{Usuario} ha sacado el picking {Pickings nuevos} de este pedido. Recárguelo antes
  de guardar.» («PickingQuitado»: «… ha quitado el picking …»); sin `Motivo` (p. ej. lo vio por GET Version), comparar `Pickings` con los que tenía.

### Ampliar un pedido en preparación (caso 931007, 18/11/26)
- Añadir líneas de PRODUCTO (id 0, cantidad > 0, sin evento, cheque ni cupón) a un pedido EN PREPARACIÓN que sale hoy ya no pasa en silencio.
  LA REGLA (pura): `AmpliacionPedidoEnPreparacion.Evaluar`; «en preparación» = la de #533 (`CambioModoConPicking.TienePickingVivo` /
  `TieneAlbaranDeHoy`). No salta en presupuestos, notas, ni si hoy no sale (`GuardiaPedidoRetenido`: retenido, aplazado, por falta): ahí se guarda y
  campana normal a Almacén. Las de texto nuevas van con los productos; las de cuenta contable nuevas se quedan. Sustituye a `ComprobarSiSePuedenInsertarLineas` (no saltaba tras el corte).
- PUT (`ServicioAmpliacionPedidoEnPreparacion.Comprobar`, antes de tocar nada): `PedidoVentaDTO.AmpliacionEnPreparacion` null → 409 `PEDIDO_EN_PREPARACION`
  (details Pickings, Envio, AgenciaYaLoTiene, Opciones, Productos); «PedidoNuevo» → en UNA transacción el original sin lo añadido (el mismo PUT) y un
  pedido nuevo por el POST de siempre (`Separar`, núcleo común; sin prepago: lo dice el aviso), `RespuestaModificacionPedidoDTO.PedidoNuevo` (y en
  UnirPedidos `PedidoNuevoAmpliacion`; la elección solo viaja en una ampliación nueva, la plantilla); «PedirAlmacen» → solo si la agencia no lo tiene
  (`YaLoTieneLaAgencia`, en la 14 = cargado en la furgoneta) ni hay etiqueta en curso con reembolso: se guarda y se pregunta a Almacén.
- PedirAlmacen: `dbo.AmpliacionesPedidoEnPreparacion` (fuera del EDMX, `Scripts/AmpliacionesPedidoEnPreparacion.sql`; sin ella, 503 y PedidoNuevo igual).
  Urgente #775 (tipo `AmpliacionPedidoEnPreparacion`, acción «pregunta», referencia = Id) a los Nesto de Almacén. `POST api/PedidosVenta/AmpliacionesEnPreparacion/{id}/Respuesta`
  {Entra} (Almacén/Dirección/Informática): «Sí» saca el picking del pedido (`GestorPicking.SacarPicking`; si no coge lo añadido, cuenta como «No»), «No» o
  60 min sin respuesta (`AmpliacionesPedidoJobs.Vencer`, Hangfire) lo pasan a un pedido nuevo (`PasoAmpliacionAPedidoNuevo`: si ya tiene picking, no se
  toca y se avisa). `notificacionResuelta` + campana/push a quien lo pidió. `GET …/AmpliacionesEnPreparacion[?pendientes=false]`, `GET …/{id}`.
- Correo: lo añadido sin picking en un pedido con picking sale como «Añadido después de sacar el picking (pendiente de que Almacén diga…)» o, en
  «Todo junto», «(sale aparte; puede salir el…)» (`GestorPresupuestos.PartidasAnadidasTrasElPicking`).
- Correo del pedido nuevo separado (pedido 931846, Carlos 27/11/26): `ConstruirPedidoNuevo` pone `SeparadoDelPedido` y, en el «No»/vencimiento (marca de «sin validar» puesta por el sistema), `ValidadoAlAnadirse` (los dos JsonIgnore): el correo dice «Pedido creado automáticamente al separar del pedido N…» y no «creado sin pasar validación»; solo si hoy no la pasaría, «Aviso: estas líneas pasaron la validación al añadirse, pero ahora no la pasarían: …». Del PUT con `CreadoSinPasarValidacion` del usuario, las dos cosas.
- **Cupón o cheque añadido con picking vivo (NestoAPI#900, pedido 930535, 25/11/26)**: en el PUT, la línea NUEVA de cupón de evento o de cheque regalo
  (−1, ALG) de un pedido con UN picking vivo (líneas -1..1 con picking) toma ese picking (y pasa a en curso) si con lo ya entregado + ese picking llega a
  su mínimo: LA REGLA (pura) `ReglaDescuentoAnadidoConPicking.Decidir` (`Models/Picking/DescuentoAnadidoConPicking.cs`), con la MISMA decisión del picking
  (`CuponesPicking`/`ChequesRegaloPicking` + `ReservarCupones`/`ReservarChequesRegalo`). No lo toma (queda para la siguiente entrega, como antes): nota de
  entrega, sin picking vivo o con varios, etiqueta en curso (cambiaría el reembolso), la agencia ya lo tiene (`YaLoTieneLaAgencia`), fecha posterior a la
  del picking o no llega al mínimo. Otras líneas negativas no se tocan. Nunca rompe el PUT (ELMAH). Sin script.

### Qué modos de servicio se ofrecen: por plan de entregas (Carlos 19/11/26)
- En Algete ya no por colores: `SugeridorModoServicio` saca los hechos de cada grupo (unidades que salen AHORA, que traen las TIENDAS y sin cubrir) con
  la misma lectura (ColorStock con distintas cantidades, en memoria con el gestor precargado) y `ModosServicioPermitidos.CalcularPorPlanes` compara el
  plan de entregas de cada modo (`PlanEntregasModoServicio`: 1 una con todo; 2 lo de ahora y una por llegada; 3 espera a TODO lo de tiendas y sale con lo
  de ahora; 4 lo de ahora y una más con el resto). Una llegada de más de una unidad puede llegar partida («*»); las tiendas van en un solo suceso (sin
  desglose por tienda). Mismo plan → se ofrece uno (1 > 4 > 3 > 2), motivo «Con este pedido es lo mismo que «X»». El 3 necesita algo de tiendas, el 4
  algo ahora; #561 sigue (nada ahora ni en tiendas → 1), con un matiz (Carlos 19/11/26): si quedan VARIAS líneas sin cubrir (pueden llegar en
  fechas distintas) se ofrece también el 2 (`ModosServicioPermitidos.OfreceSegunVayaEntrandoSinNadaDisponible`); con una sola, solo el 1. Caso A (verde + entera en Reina): 3 = 1, no se ofrece. Caso B (2 uds., 1 en Reina): 3 sí.
  Una sola unidad sin cubrir: 2 = 4, solo el 4. El sugerido (regla de siempre, `Decidir`) se cambia a su equivalente ofrecido (`Ofrecido`). Tienda,
  otros almacenes, modo 6 y picking no cambian; al modificar, la comprobación tolerante de siempre (sin cambio de modo no se mira).

### Muestras o regalos sin stock en un pedido de TIENDA: el mensaje (incidencia 1460, Sancho, 25/11/26)
- En tienda solo vale el 2 y la API corrige en silencio «Todo junto» al 2 (`AplicarModoCorregido`, ahora marca `PedidoVentaDTO.ModoCorregidoPorTienda`,
  JsonIgnore); luego `ValidadorMaterialPromocional`/`ValidadorDisponibilidadRegalos` frenan el 2 y decían «elija otro modo («Todo junto»…)»: sin salida.
  Solo TEXTO (`TextosServirJuntoTienda`; tienda = `ModosServicioPermitidos.ClasificarAlmacen`): en tienda la salida es «bórrelas del pedido o páselo
  desde Algete (ALG)» y, con el modo corregido (llega por `ValidarServirJuntoRequest.ModoCorregidoPorTienda`, JsonIgnore, POST y PUT), «El pedido es
  de Alcobendas y allí solo puede salir «Según vaya entrando». No hay stock en Alcobendas de …: bórrelas del pedido (…) o páselo desde Algete (ALG).»
  En Algete, el texto de siempre. Reglas, `ProductosProblematicos` y `ValidarServirJunto` de pantalla (solo cambia su texto en tienda), igual.
- **Al GUARDAR, las muestras sin stock de un pedido de tienda se quitan solas (OK de Carlos 25/11/26)**: si la denegación es de `ValidadorMaterialPromocional`
  (`ValidarServirJuntoResponse.DenegadoPorMuestras` y `ProductoSinStockDTO.Disponibles`, JsonIgnore) en REI/ALC, con el interruptor `GestorMuestrasSinStock.CLAVE`
  y sin ser presupuesto ni nota, `PedidosVentaController.ValidarServirJuntoQuitandoMuestrasDeTiendaAsync` (POST y PUT, en el sitio de la validación de
  siempre: ANTES de portes, precios y líneas) aplica LA REGLA (pura) `ReglaMuestrasSinStockTienda` (`Infraestructure/PedidosVenta/MuestrasSinStockTienda.cs`)
  con la del picking (`GestorMuestrasSinStock.UnidadesQueSalen`: sin stock, fuera; parcial, lo que hay; el disponible lo gastan primero las intocables y luego
  por orden) y vuelve a validar (un regalo sin stock sigue dando su 400). En el PUT solo se tocan líneas nuevas o pendientes sin picking, y si el pedido ya
  tiene picking, solo las nuevas (`SePuedeQuitarMuestraAlModificar`); si lo que falla está en curso, el 400 de hoy. Solo muestras: el 400 de hoy + «Como el pedido
  no lleva nada más que muestras, no se quitan solas: se quedaría vacío.». Aviso `MuestrasSinStockQuitadas` en `AvisosGrabacion` (POST) / `Avisos` (PUT) y en el
  correo («En Alcobendas no hay stock de estas muestras y se han quitado del pedido: 39918 BOLSA…. De 42654 … solo hay 1 de 3; se ha dejado en lo que hay. Si las
  necesita, pase el pedido desde Algete (ALG).»); rastro en Modificaciones (Tabla LinPedidoVta, como el picking, usuario = quien guarda, Operacion «Pedido de
  tienda: muestra sin stock quitada al guardar») en el mismo guardado + ELMAH. `ValidarServirJunto` de pantalla, sin cambios (Nesto sigue preguntando).

### Modo de servicio 6, «Junto con lo pendiente del cliente» (sugerencia 1340, 18/11/26; Nesto gemelo)
- `ModosServicio.JUNTO_CON_LO_PENDIENTE` = 6: un «todo junto» (ServirJunto = 1, `EsTodoJunto`/`EsEntregaUnica` true, facturación como el 1) que
  además no sale hasta que pueda salir lo pendiente del MISMO empresa+cliente+contacto. `Efectivo(6, true)` = 6; ServirJunto desmarcado → 2.
  `EsValido` sigue siendo 1..4 (parámetro ModoServicioPorDefecto); `EsElegible` = 1..4 + 6. Nunca se sugiere.
- LA REGLA (`Infraestructure/PedidosVenta/JuntoConPendiente/ReglaJuntoConPendiente`): cuenta lo que tenga alguna línea de producto en ALG,
  -1/1, con cantidad y SIN picking (también 31/12/2099: las notas nacen así). No cuentan el propio, presupuestos, cursos (van en la tienda),
  notas de cobro/recogida (sin producto), retenidos (5) ni otros 6 (dos 6 no se esperan). Lectura en UNA consulta (`RepositorioJuntoConPendiente`).
- Script `Scripts/Sugerencia1340_ModoJuntoConLoPendiente.sql`: amplía CK_CabPedidoVta_ModoServicio a 1..6 (lección #698) y crea
  `PedidosJuntoConPendienteAvisos`. Sin él (`ModoAdmitido`, caché 5 min): no se ofrece, elegirlo → 400, el picking y el job no hacen nada.
- Se OFRECE (ModoServicioSugerido y OpcionesModos, copia de la sugerencia, la caché no se toca) solo si hay algo con lo que juntarse o el pedido ya
  está en 6, nunca en tienda; `ModoServicioPermitidoDTO.Explicacion` («Saldrá junto con la nota de entrega 929701.») y `PedidosConLosQueSeJunta`.
  POST/PUT: ENTRAR en el 6 sin nada pendiente o sin script → 400 `MODO_SERVICIO_NO_PERMITIDO` (alternativa 1); quien devuelve el 6 no se rechaza.
- Picking (`Models/Picking/JuntoConPendientePicking`): el rellenador arrastra los 6 del mismo contacto que un candidato y lo pendiente de los 6
  (picking de un pedido, de un cliente o de rutas). El 6 se decide DESPUÉS de los demás: sale si todo lo suyo está en la pasada y sale
  (`MotivoNoSalePicking.EsperaPendienteDelCliente` si no); mismo número de picking → un envío en el packing. Sin nada pendiente, como el 1.
- Portes (decisión 7): al grabar, la base de lo pendiente se suma y si lo pendiente ya lleva portes el 6 no los añade; en el picking el grupo
  que sale junto suma lo que se factura (`ImporteEntregaJunto`) y paga portes UNA vez (`PortesYaEnElGrupo`).
- Fecha (#606): el motor calcula el 6 como 1 y cada pendiente con su modo; sale el día del PEOR (`ReglaFechaJuntoConPendiente`,
  `FechaEntregaAgenciaDTO.JuntoConPendiente`): «Sale el lunes 30/11/2026, junto con la nota de entrega 929701, que espera el … (45238).» Va al pie
  del correo y a la promesa en riesgo (culpable propio; `SQL_CANDIDATOS` incluye los 6 cuyo contacto tiene pendiente el producto).
- Job `pedidos-junto-con-pendiente-esperando` (L-V 9:30): 15 días en 6 (desde PedidosModoServicioOrigen #704; si no, la fecha del pedido) y
  aún esperando → campana/push a quien lo hizo y push a su vendedor, una vez (`PedidosJuntoConPendienteAvisos`).

### Faltas al terminar el picking: aviso y muestras fuera (Novedades 1228, 12/11/26)
- `OrigenSalidaPicking.Terminar`: una falta de una MUESTRA (LinPedidoVta.SubGrupo MMP, no nota de entrega, interruptor
  `GestorMuestrasSinStock.CLAVE`) se QUITA del pedido (`ITransaccionSalida.QuitarDelPedido`: total = línea borrada; parcial = se queda
  con lo cogido y lo que falta no queda pendiente) con rastro `EscritorMuestrasSinStock.ModificacionFaltaPicking`, en la misma
  transacción; solo si del pedido sale algo más que muestras (si no, pendiente como siempre). Lo demás queda pendiente como hoy.
- Ya guardado, `IAvisadorFaltasPicking` (en segundo plano, nunca rompe el Terminar): por pedido, texto puro `AvisoFaltasPicking`
  (tipo `PickingConFaltas`, Datos pedido → NestoApp lo abre sin cambios) al buzón de quien hizo el pedido + su push de NestoApp, y push al
  vendedor si es otro (reglas de #688); a Compras (`TIPO_PICKING_FALTA`) lo pendiente sin compra que no es muestra. Ariadna: sin cambios
  (`ResultadoTerminarSalidaDTO.FaltasPorAvisar` es JsonIgnore).
- **«No está» = hueco a cero (C1, Carlos 12/11/26)**: `POST api/Almacen/Recogidas/{tipo}/{n}/NoEsta` {Producto, Hueco, Cantidad}
  (`ServicioNoEstaSalida`, núcleo común + `IOrigenNoEsta` por tipo: PICK y REPO, los dos únicos tipos de salida). En UNA transacción: applocks `InventarioRellenar:`,
  la reserva propia del hueco vuelve a libre, fila de Inventarios (usuario técnico «Ariadna (No está)», contado 0) y
  prdContabilizarInventario por su único call site (solo esas filas); cuadre comprobado (sin pendientes, hueco sin libre, extracto = lo
  libre) o se deshace. Luego reserva en el siguiente hueco (`HuecoPropuesto`) o, sin más, falta definitiva YA (`QuitarFaltasDeLosPedidos`,
  con A y B). `Regularizado` false = no se ha tocado nada (tienda, se está inventariando, interruptor…) y la app da la falta como
  siempre; 409 si falla. `?ensayo=true` (Admin/Dirección). Ariadna aún no lo llama.
- **REPO y ENCENDIDO (NestoAPI#858, Carlos 12/11/26)**: el origen solo pone (a) la reserva propia y cómo se suelta, (b) cómo se reserva
  en el siguiente hueco y (c) la falta definitiva; lo demás es núcleo. REPO (`OrigenNoEstaReposicion`): (a) estado 4 del traspaso
  (NºTraspasoRepo; las -4 de Nesto viejo no) → libre en su hueco por la MISMA escritura que el picking; (b) estado 4 con su NºOrdenRepo
  como CambiarHuecoReservaReposicion (`SQL_RESERVA_DE_LA_LIBRE_PARA_EL_TRASPASO`), sin «pendiente de ubicar»; (c) menos en la salida y
  en la entrada de PreExtrProducto SIN tocar Ubicaciones (`TransaccionSalidaSql.QuitarDelTraspaso`: el inventario ya la quitó del stock).
  Si era lo último por sacar del traspaso, `Regularizado` false (el Terminar no encontraría la salida). Interruptor en ParametrosUsuario
  empresa 1 «(defecto)» `Salidas.NoEstaRegulariza`: sin fila o «1» = encendido; «0»/false/no/off/apagado = apagado al momento
  (`Scripts/Issue858_NoEsta_Interruptor.sql`, opcional; el appSettings ya no se lee). Cada uno regularizado: rastro + línea «[No está] …» en ELMAH.

### «Todo junto» con falta de pago: pedido retenido por falta (NestoAPI#858 punto 4, 12/11/26)
- En `OrigenSalidaPicking.QuitarFaltasDeLosPedidos` (Terminar y falta definitiva del «No está»), dentro de su transacción
  (`ReglasRetencionTodoJunto.RetenerSiHaceFalta`, solo si la tx implementa `IRetencionTodoJuntoSalida`, como `TransaccionSalidaSql`): una falta
  que queda pendiente (no muestra ni quitada) de una línea con base > 0 y no ficticia, de un pedido en modo efectivo 1 del que sale algo más →
  fila «Pendiente» en `dbo.PedidosRetenidosPorFalta` (`Scripts/Issue858_PedidosRetenidosPorFalta.sql`; sin él, nada y ELMAH una vez).
  Plazo: `Picking.TodoJuntoPlazoHoras` (2) horas laborables L-V 8-18 sin festivos de Algete. Interruptor `Picking.TodoJuntoRetener` (apagado con «0»).
- Bloqueo con «Esperando al vendedor: no empaquetar…»: `GuardiaPedidoRetenido.Motivo` de producción (albarán, Facturar rutas, etiqueta del
  packing → 409) y cerrar caja/foto de bulto (`GuardarBulto`, 409). El packing marca `RetenidoPorFalta`, `AvisoRetencion`, `PlazoRetencion`.
- `GET api/PedidosVenta/RetenidosPorFalta`, `POST api/PedidosVenta/{empresa}/{pedido}/RetencionPorFalta/Decidir` {Decision ServirLoQueHay|Esperar}
  (quien hizo el pedido, su vendedor o Almacén/Administración/Dirección/Informática). «Esperar» solo con el picking terminado: las reservas del
  pedido pasan a «pendiente de ubicar» (el mozo las ubica con la PDA) y Picking = 0. Job `pedidos-retenidos-por-falta` (L-V cada 15 min 8-18):
  vencidas → Esperar + aviso `PedidoRetenidoPorFaltaEsperar`. El aviso `PickingConFaltas` lleva Datos `retenido`="true" y `plazo`.

### Saldo a favor y pedidos de prepago en el picking (Carlos 17/11/26)
- Un pago es de UN documento (Carlos 17/11/26): cada saldo a favor (ImportePdte < 0) se clasifica por los números de su Concepto (y, solo
  como pedido, su Nº Documento) contra los pedidos y facturas DEL MISMO cliente (`ClasificadorSaldoAFavor`, puro; el rellenador lee esos
  números en una consulta por cosa). Reservado a un pedido = solo cubre ese pedido, aunque no salga o ya esté facturado; reservado a una
  factura = no cubre ningún pedido; sin documento del cliente («curso WOW», 987654 de otro cliente, fechas) = saldo LIBRE.
- Disponible de un pedido PRE = sus Prepagos + lo reservado a él + su parte del saldo libre (± `DESCUADRE_PERMITIDO`). Libre = sin documento −
  deuda vencida (#815 igual) − lo que se llevan los otros PRE del cliente: los de pickings anteriores sin facturar (max(0, importe en picking −
  sus prepagos − lo reservado a ellos), `IRellenadorPrepagosService.PedidosPrepagoEnPicking`, una consulta) y, en la misma pasada, los de
  número menor que salen. Regla pura `ReglaSaldoAFavorPrepago`; acumulador `RepartoSaldoAFavorPrepago` (GestorPicking lo prepara y decide por
  número; el que no sale suelta su parte del LIBRE). Antes cada pedido veía el saldo entero (930831 y 930835 del 42004, picking 100105).

### Pago a plazos de un pedido ya creado, con cualquier financiera (NestoAPI#864, 13/11/26; continuación de #663)
- Un empleado (Nesto) o un vendedor (NestoApp) pasa el pedido y el CLIENTE termina la financiación de ese pedido en la app (push) o con
  un enlace. TODO NEUTRO (Carlos 13/11/26): el núcleo `ServicioPagoAPlazos` (`Infraestructure/Pagos/PagoAPlazos/`: pedido, prepago, estados,
  canal, avisos, plazos) solo habla con `IProveedorPagoAPlazos` (condiciones y límites, crear la solicitud, URL de pago, interpretar y
  contestar la notificación, consultar/capturar, cancelar, reembolsar). Aplázame = `Pagos/Aplazame/ProveedorPagoAPlazosAplazame` (sobre
  `IClienteApiAplazame`). **Otra financiera** = una clase con la interfaz + una línea en `RegistroProveedoresPagoAPlazos.PorDefecto` + su
  código en el parámetro `PagoAPlazosProveedor`. Lo de la app de #663 (api/Pedidos/Cliente/…/Aplazame, PagosTPV, job `aplazame-capturas`) NO cambia.
- `dbo.SolicitudesPagoAPlazos` (fuera del EDMX, `Scripts/Issue864_PagoAPlazos.sql`; sin él los endpoints dan 503 y el job no hace nada).
  Estados: Pendiente → Aprobado → Capturado; Rechazado, Cancelado, Caducado. Referencia ante el proveedor `PAP-{Id}`. Parámetros «(defecto)»:
  `PagoAPlazosPedidos` (Apagado/Pruebas/Activo; interruptor PROPIO, no mira `AplazameApp`), `PagoAPlazosPedidosClientesPrueba`,
  `PagoAPlazosProveedor` (Aplazame), `PagoAPlazosDiasAviso` (3), `PagoAPlazosDiasValidezSolicitud` (7). Límites y plazo de captura: del proveedor.
- `GET api/PedidosVenta/{empresa}/{pedido}/PagoAPlazos/Disponible` (siempre 200: Disponible, Motivo, Proveedor, NombreProveedor, Importe,
  límites, DiasPlazoCaptura, ClienteTieneApp, Canal sugerido, Aviso), `POST …/PagoAPlazos` {Canal? App|Enlace, Correo?} (409 no disponible o
  App sin app; 400 canal/correo; 502 el proveedor falla; UrlPago si es Enlace o si la push no sale), `GET …/PagoAPlazos/Estado` (SinSolicitud…),
  `DELETE …/PagoAPlazos?motivo=`. Permisos (`PermisosPagoAPlazos`): como los enlaces de pago, menos los clientes de la tienda (403) y el
  vendedor de NestoApp solo con pedidos de sus clientes. App de clientes: `GET api/Pedidos/Cliente/{pedido}/PagoAPlazos` (UrlPago del WebView).
- Disponibilidad (`ReglasPagoAPlazos.MotivoNoDisponible`, pura): interruptor, proveedor operativo, ni nota de entrega ni presupuesto ni
  facturado, sin otra financiación concedida, pendiente (líneas -1/1/2 − prepagos − enlaces cobrados) > 0 y dentro de los límites, y todas las
  líneas sin servir con Fecha_Entrega ≤ hoy + plazo de captura (NO mira stock). `ClienteTieneApp` = dispositivo activo de NestoTiendas.
- Notificación: `POST api/PagoAPlazos/Notificacion/{proveedor}` (anónimo; la valida el proveedor). Aprobación = UPDATE condicional
  Pendiente→Aprobado con el importe EXACTO + Prepago (cuenta y prefijo del proveedor; Aplázame: los de #663) con `Prepagos.FechaCobro` (#860)
  = día de la aprobación, idempotente por el concepto; si el pedido ya debe menos de lo pedido, no se confirma. NO toca la forma de pago.
- Job `pago-a-plazos-pedidos` (L-S cada hora 8:35-20:35): todo en albarán → captura lo servido (nunca más de lo aprobado, preguntando antes
  lo ya capturado); a `PagoAPlazosDiasAviso` del límite sin salir → aviso (una vez); plazo vencido → cancela en el proveedor y QUITA el
  prepago (con parte servida: captura lo servido y ajusta el prepago); pedido borrado (también en `DELETE api/PedidosVenta`) → cancela;
  pendiente sin terminar a los 7 días → Caducado.
- Avisos (`TextosAvisosPagoAPlazos`): al cliente push `PagoAPlazosPendiente` (de tú; Datos tipo, empresa, pedido, idSolicitud, cliente,
  estado, proveedor, importe); a quien hizo el pedido, a quien pidió la financiación y a su vendedor (campana + push NestoApp)
  `PagoAPlazosAprobado`, `PagoAPlazosRechazado`, `PagoAPlazosPlazoCerca` (+fechaLimite, dias) y `PagoAPlazosPerdido`.
  El resultado también al CLIENTE (13/11/26; mismos Datos y dispositivos que el pendiente, sin app nada, TNV#99 los abre igual):
  `PagoAPlazosAprobadoCliente` (con el prepago recién creado, como el del vendedor) y `PagoAPlazosRechazadoCliente` (al pasar a Rechazado).

### Formación sin evento en la plantilla de venta (incidencia 1398, Pilar, Carlos 20/11/26)
- LA REGLA (pura) `ReglasFormacionPlantilla.EsFormacion` (`CursosPlantilla.cs`): grupo CUR que no es la referencia de un evento (EVENTO00123[NR]) ni la
  plantilla de los cursos (`ReglasProductoCurso.Plantilla`, 90077): 90324, 90047, MF…, IMPE…. La búsqueda de la plantilla (`BuscarProducto` y `Buscar`,
  vía `CatalogoCursosPlantilla.LineasCursos`) la saca (Estado ≥ 0) con `LineaPlantillaVenta.EsFormacion` = true y `EsCurso` = false (no se pide evento);
  los cursos de evento, como siempre (EsCurso).
- Al guardar (POST PedidosVenta): formación + productos (no presupuesto, sin cursos de evento) → `PreparacionCursos.Formacion` y
  `OrquestadorPedidoConFormacion`: los productos por el cauce de siempre y la formación, sola, por el MISMO cauce (`CopiaParaFormacion`: la cabecera
  sin prepagos, efectos ni sesión de sugerencias), que `SerieSegunLineas` pone en CV como hoy un pedido de solo formación. Respuesta: la del de
  productos con `PedidosAdicionales` [{Numero, Motivo «Formación»}]; si la formación falla, los productos se quedan y va en `AvisosCursos`. Solo
  formación: UN pedido (como antes). Sin inscripción, señal ni enlace: precio y descuento de quien la vende, con las validaciones de siempre.
  Formación en un presupuesto o con un curso de evento: el 400 de antes.

### Cambiar el cliente de un pedido con picking y ensayo (NestoAPI#876, Carlos 26/11/26)
- `POST api/PedidosVenta/{empresa}/{numero}/CambiarCliente` (#519, núcleo `GestorCambioClientePedido`, ahora también en `CambiarClienteDelPedido` para
  api/Clientes/CambioFiscal/PasarPedidos): el picking ya NO bloquea siempre. LA REGLA (pura) `ReglaCambioClienteConPicking` (`CambioClienteConPicking.cs`):
  un pedido con picking (sin albarán, factura ni envío de agencia: esas siguen parando, `CambioClientePedido.MotivoNoSePuede(…, admitirPicking: true)`)
  se cambia si y solo si, recalculado «en seco» para el cliente nuevo, ninguna línea cambia de precio, descuentos (producto, cliente, pronto pago,
  línea), IVA ni importes (al céntimo; precio y descuentos a 4 decimales) y la cabecera no cambia de IVA. Si algo cambiaría: 409 `{Message, Codigo:
  "CAMBIO_CLIENTE_CON_PICKING_CAMBIA"}` diciendo qué líneas y qué, y todo se deja como estaba (foto y `Restaurar`, sin SaveChanges).
- Con picking y sin cambios: líneas primero y luego la cabecera (como siempre), validación con el cliente nuevo, rastro en Modificaciones («CambiarCliente
  con picking sin cambios de importes (#876)» + pickings), SIN el PUT (que en un pedido con picking libera y recalcula portes y reembolsos) y con la
  ruta de antes (el picking ya es de esa ruta; lo dice `Avisos`). El picking y las reservas no se tocan. Después, campana a Almacén (`AvisadorRetencionAlmacen`,
  tipo `PedidoCambiaDeCliente`, Datos empresa/pedido/clienteAnterior/cliente/contacto/pickings; nunca rompe).
- `CambiarClientePedidoRequest.Ensayo` (aditivo): calcula en seco y devuelve `Cambios` (cabecera y líneas) sin guardar nada; los portes no se calculan en
  el ensayo (aviso). Respuesta (aditivo): `Ensayo`, `SinCambiosDeImportes`, `TeniaPicking`, `Avisos`.
- Nesto: el 409 con Codigo trae el texto para enseñarlo tal cual (hoy pinta el 400 de «quite el picking»); NestoApp no lo usa.

### Modificar un pedido con la plantilla: descuento de producto de cada línea (incidencia 1504, María José, pedido 931500, 27/11/26)
- `GET/POST api/PedidosVenta/ParaPlantilla` (`ConvertidorPedidoAPlantilla`) manda en `LineaParaPlantillaDTO.DescuentoProducto` (aditivo) el grabado en la línea de
  pago (en una oferta, el de la de pago; 0 si solo quedó la regalada), junto a `Descuento` (sigue siendo el de LÍNEA) y `AplicarDescuento`: ahí van los de campaña y
  el 100 % de las muestras. Nesto (`LineaParaPlantillaModel.DescuentoProducto As Decimal?`) lo usa si llega y si no hace una segunda lectura del pedido; NestoApp lo ignora.
