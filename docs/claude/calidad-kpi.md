# Calidad: KPI del pedido perfecto y señales de calidad

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Épica NestoAPI#914 (KPI de calidad, Carlos 26/11/26).
Este fichero es LA REFERENCIA de las definiciones de toda la épica: si una definición cambia, cambia aquí (con fecha y motivo).
Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.

### Principios (Carlos 26/11/26)
- NADA manual: ni errores ni costes los apunta nadie; todo sale de lo que ya hace el sistema.
- Primero en sombra (solo lectura, Dirección e Informática). Sin nombres de personas en lo agregado (el detalle por pedido sí lleva vendedor y
  usuario, porque el corte 5 los usará).
- Pedido perfecto = «lo meto, tenemos stock de todo, lo servimos al día siguiente, se cobra al contado y no hay ningún problema». Todo lo que se
  salga es un MOTIVO (puede haber varios). Los pedidos que no pasan la validación no llegan a ser pedido: no entran.

### Pedido perfecto, corte 1 (NestoAPI#914, 26/11/26)
- `GET api/Calidad/PedidoPerfecto/Resumen?empresa=1&desde=&hasta=` → {Empresa, Desde, Hasta, Pedidos, Perfectos, PorcentajePerfectos,
  PerfectosSinContarInformativos, PorcentajePerfectosSinContarInformativos, Motivos[{Motivo, Texto, EsError, Evaluado, EvaluadoDesde, PedidosEvaluados,
  Pedidos, Porcentaje}], PorMes[{Mes «yyyy-MM», Pedidos, Perfectos, Porcentaje}], PorCanal[{Canal, Pedidos, Perfectos, Porcentaje}],
  MotivosNoEvaluados[{Motivo, PorQue, EvaluadoDesde, PedidosSinEvaluar}], CalculadoEl, Aviso (tablas opcionales que faltan)}.
- `GET api/Calidad/PedidoPerfecto/Pedidos?empresa=1&desde=&hasta=&motivo=&pagina=1&tamano=100` → {Empresa, Desde, Hasta, Motivo, Pagina, Tamano,
  Total, Pedidos[{Empresa, Pedido, Fecha, Cliente, Contacto, Canal, Vendedor, Usuario, UsuarioEsDelAlta, Importe, Perfecto,
  PerfectoSinContarInformativos, Motivos[], MotivosNoEvaluados[], PrimerAlbaran, DiasLaborablesHastaServir, Albaranes, ModoServicio, PlazosPago,
  FormaPago, LlevaLineasNegativas}]} ordenado por fecha y pedido. `motivo` = nombre del enum (sin distinguir mayúsculas; un número o un nombre que no existe → 400 con la lista).
- `CalidadPedidoPerfectoController`: solo Dirección e Informática (403 de usted). Periodo [desde, hasta): por defecto de hoy − 12 meses a hoy − 21 días
  (para que hayan tenido tiempo de servirse y cobrarse); desde < hasta, como mucho 13 meses, hasta ≤ mañana (400 de usted). Si la lectura falla: 503 y ELMAH.
- Código: `Infraestructure/Calidad/PedidoPerfecto/`: LA REGLA (pura) `ReglaPedidoPerfecto.Evaluar(HechosPedidoPerfecto, ContextoPedidoPerfecto)`, el
  enum `MotivoPedidoPerfecto` con su atributo (Texto de usted, `EsError`), el lector `LectorPedidoPerfectoSql` y `ServicioPedidoPerfecto` (resumen,
  página y caché en memoria de 30 min por empresa+periodo, compartida por los dos GET; si la lectura falla no se cachea). Sin tabla nueva ni job.
- Lector: SOLO LECTURA (READ UNCOMMITTED + NOLOCK), una ida por MES (tabla temporal #p con los pedidos del mes, #f con sus facturas y #r con lo rectificado de ellas; varios
  resultados en el mismo lote) + tres consultas para todo el periodo (Modificaciones, CalidadEventos, primera foto). Sin N+1. Las tablas fuera del
  EDMX son opcionales (OBJECT_ID): sin ellas su parte no se lee y lo que dependa queda «no evaluado» (y sale en `Aviso`).
- «No evaluado»: por PEDIDO, un motivo puede no poderse mirar (no hay rastro de esa época, plazo sin vencer…). Un motivo no evaluado no cuenta ni a
  favor ni en contra: Perfecto = ningún motivo entre los que SÍ se han podido mirar. El `Porcentaje` de cada motivo es sobre sus `PedidosEvaluados`.
  `EvaluadoDesde` = desde cuándo hay rastro (null = siempre); los pedidos con Fecha anterior no se evalúan para ese motivo.

#### Universo
Empresa del parámetro (1 por defecto); `CabPedidoVta.Fecha` en [desde, hasta); con alguna línea de producto (`TipoLinea = 1`) con `Cantidad > 0`
(queda fuera solo el abono puro); sin líneas de presupuesto (Estado −3);
`TipoRectificativa` vacío y `PedidoOrigen` NULL (ni rectificativas ni copias); y `NotaEntrega = 0` (decisión de este corte: las notas de entrega
procesan a −2 y nunca tienen albarán; saldrían todas «sin servir»). Los clientes especiales NO se quitan: van clasificados en `Canal`.
- Líneas negativas (Carlos 26/11/26, cambia el corte 1): antes se quitaba el pedido con ALGUNA línea en negativo; en 10 semanas eran 358 de ~3.800 y
  la mayoría no eran abonos: cheque regalo (CHEQUE50_OCT26, TipoLinea 1, −1), «TiCKET Suscríbete y ahorra», descuento de portes (TipoLinea 2, 624…)
  o una devolución suelta dentro de un pedido normal. Ahora ENTRAN. Servido, pendiente, albaranes, primer albarán, fecha de entrega, almacén,
  tienda y cursos miran solo las líneas de producto con `Cantidad > 0` (el −1 del cheque no es «pendiente» ni otro albarán); el `Importe` suma todas.
  El detalle por pedido lleva `LlevaLineasNegativas` (alguna línea con cantidad < 0).

#### Canal (con lo que ya hace el código; el primero que case)
1. `Interno`: El Edén (15191, `ClientesEspeciales.EL_EDEN`) o materiales de cursos (31794).
2. `Amazon`: alguna línea con Forma Venta STK (`FormasVenta.AMAZON`) o cliente 32624.
3. `TiendaOnline`: alguna línea WEB o cliente 31517 («FACT. SIMP. VENTAS TIENDA ONLINE»).
4. `OtroCanalExterno`: alguna línea QRU (Perfumes Club) o BLT (Miravia).
5. `App`: alguna línea APP (pedidos del cliente en TiendasNuevaVision).
6. `Cursos`: todas las líneas de producto del grupo CUR.
7. `TiendaFisica`: alguna línea de producto con almacén REI o ALC, o cliente 9500 «PEDIDO TIENDA».
8. `Otros`: cliente 10458 (público final).
9. `Vendedor`: el resto.

#### Motivos (definiciones exactas)
| Motivo | EsError | Cómo se calcula | Desde cuándo |
|---|---|---|---|
| `NoServidoDeUnaVez` | sí | Algo servido (línea de producto en estado ≥ 2) y (más de un `Nº Albarán` distinto entre ellas, o alguna línea de producto aún en −1/1). | Siempre |
| `SinServir` | sí | Ninguna línea de producto en estado ≥ 2, alguna en −1/1, y han pasado 21 días naturales desde la referencia (ver abajo). Dentro del plazo o con Fecha Entrega 31/12/2099: no evaluado. | Siempre |
| `NoSalioAlDiaSiguiente` | sí | Días laborables (sin sábados, domingos ni festivos del ALMACÉN de las líneas: `GestorFestivos.EsFestivo`) d con referencia < d ≤ primer `Fecha Albarán`; > 1 → motivo. Pedido del viernes servido el lunes = 1 (perfecto); metido en sábado, el lunes hace de día del pedido. No evaluado: sin albarán, modo 6 o sin fecha (2099). | Siempre |
| `FaltabaStock` | sí | Alguna foto de `dbo.FechasEntregaFotos` del pedido con `FechaFoto` ≤ Fecha + 3 días y `Origen` Tienda, EnCamino, Proveedor o SinStock (lo que no sale del stock de Algete). | Primera `FechaFoto` (11/11/26) |
| `NoCobradoAlContado` | NO (informativo) | `PlazosPago` distinto de CONTADO, CR (contado riguroso) y PRE (prepago). Sin plazos: no evaluado. | Siempre |
| `EntregaIncompleta` (13) | sí | Otro pedido con `PedidoOrigen` = este que es la NOTA DE ENTREGA de lo pendiente: su `NotaEntrega` = 1 (marca firme) o, de respaldo, su `Comentarios` contiene «NOTA DE ENTREGA: pendiente de entregar del pedido N » con N = este pedido. Ver abajo. | Siempre (pero el enlace `PedidoOrigen` solo existe desde #542, 25/09/26: lo albaranado antes no puede salir) |
| `FacturaRectificadaEntera` (14) | sí | Alguna de sus facturas (`Nº Factura` de sus líneas) es `FacturaOriginalNumero` de `LinFacturaVtaRectificacion` (con otra `NumeroFactura`) y las unidades rectificadas ≥ las unidades de producto (TipoLinea 1, Cantidad > 0) de ESTE pedido en esa factura (> 0). Rectificadas = Σ ABS(`CantidadRectificada`) de las filas cuya `FacturaOriginalLinea` (= `Nº Orden`) es una línea de producto de este pedido en esa factura, más las filas que no casan con ninguna línea de la factura si la factura solo lleva este pedido. | Siempre |
| `FacturaRectificadaEnParte` (15) | sí | Alguna factura rectificada (como arriba) y ninguna entera. Excluyentes: rectificadas = entera + en parte. | Siempre |
| `TieneCopia` (16) | NO (informativo) | Otro pedido con `PedidoOrigen` = este que NO es nota de entrega (ni marca ni comentario). Hoy no debería haber: solo #542 rellena `PedidoOrigen` (la copia de pedidos no, `GestorCopiaPedidos`); está para no perderla de vista. | Siempre |
| `Impagado` | sí | `ExtractoCliente` TipoApunte 4, Importe > 0, Concepto que no empieza por «Gastos Impagado» (como `ModeloImpagados`), mismo empresa + cliente + `Nº Documento` = su factura. | Siempre |
| `IncidenciaDeAgenciaOReenvio` | sí | Envíos del pedido con Estado ≥ 1: más de uno, o alguno en 3 (incidentado), 4 (devuelto) o 5 (cerrado a mano), o con «Reenviar» en `EnviosAgenciaReenvios` (#699), o su caja «ha vuelto» (`EnviosAgenciaHaVuelto`, #870). | Siempre (lo que hay HOY: un incidentado que acabó entregado no deja rastro en GLS) |
| `TocadoTrasElPicking` | sí | «Quitar picking» de Nesto en Modificaciones (Tabla LinPedidoVta, Nuevo «Pedido N…: sin picking (quitado por …») o eventos `QuitarPicking` / `AmpliacionConPickingMismoPedido`. | 09/11/26 (#590) o el primer evento, lo que sea antes |
| `EnvioDeRutaPropiaForzado` | sí | Evento `EnvioRutaPropiaFueraDeDia`. SIN CONECTAR: no hay un punto único donde se fuerce; hoy siempre no evaluado. | Primer evento |
| `EntregaIncompletaDelReparto` | sí | Envío de la agencia 14 con DetalleEstado «Entregado (reparto propio): …» (#880 corte 1) o con filas en `RepartoBultosPendientes` (#880 corte 2). Sin envío de la 14: no aplica (falso, evaluado). | Envíos de la 14 desde el 19/11/26 |
| `PidioCambiarAlgoConPicking` | sí | Eventos `CambioDeModoConPicking` o `AmpliacionConPickingPedidoNuevo`. | Primer evento |
| `ErrorDeEnvioReclamado` (17) | sí | Alguna línea de producto servida del pedido (TipoLinea 1, Cantidad > 0, estado ≥ 2 con Fecha Albarán, sin ficticios ni cursos) con `ReenviadoSinCoste` o `CambiadoPorOtro` (ver «Error de envío reclamado» abajo: la MISMA regla y lectura que Almacén). No evaluado: nada servido, o alguna línea servida hace menos de `Calidad.DiasReclamacionEnvio` días (salvo que otra ya tenga el error). El detalle lleva `SubMotivosReclamacion` y `PruebasReclamacion`. | Siempre (27/11/26) |

**Error de envío reclamado y las rectificativas (27/11/26).** Un pedido con `CambiadoPorOtro` tiene SIEMPRE también `FacturaRectificadaEntera`
o `FacturaRectificadaEnParte` (el cambio sale de una línea rectificada): son dos motivos del mismo pedido, uno dice «hay rectificativa» y otro
«y fue porque se mandó otra cosa». El porcentaje de perfectos no cuenta nada dos veces (un pedido es perfecto o no), pero la suma de los
`Pedidos` de cada motivo sí puede pasar del total. `ReenviadoSinCoste` no tiene por qué tener rectificativa: es justo el pedido que se tuvo que
reenviar y antes salía «perfecto». `AbonadoSinReposicion` (rectificada sin reposición) NO es este motivo: ya lo dicen los de rectificativa.

**Rectificativas, notas de entrega y copias, partidos (Carlos 27/11/26).** El antiguo `TieneRectificativaOAbono` (6) mezclaba cosas distintas: de
27/08 a 05/11/26 (empresa 1, 3.645 pedidos) lo tenían 204; 46 por «copia», que eran todas NOTAS DE ENTREGA de lo pendiente (entrega
incompleta), y 161 por factura rectificada (85 enteras, 13 en más de la mitad, 64 en menos). Se partió en los cuatro de arriba; el 6 NO se
reutiliza y el nombre antiguo ya no existe (`?motivo=TieneRectificativaOAbono` → 400). Los tres primeros son error, como el antiguo: el
porcentaje de perfectos no cambia; solo una copia que no sea nota deja de ser error (sí sigue quitando el «perfecto» a secas, como `NoCobradoAlContado`).
- Cómo se reconoce la nota: el ÚNICO sitio que rellena `PedidoOrigen` es `CreadorNotaEntregaPendiente` (#542, tras el albarán con líneas
  `Recoger`: el pedido se factura entero y lo no entregado va a una nota), siempre con `NotaEntrega` = 1, `AlbaranOrigen` y el comentario
  (`PREFIJO_COMENTARIO`). Por eso manda la marca; el comentario (de ESE pedido, no de otro) es solo respaldo. Regla: `ReglaPedidoPerfecto.EsNotaEntregaDeLoPendiente`.
- NO se pisa con `EntregaIncompletaDelReparto` (11): ese es el repartidor de la 14 que llega con bultos de menos (#880, DetalleEstado o
  `RepartoBultosPendientes`), no crea nota; este es lo que se queda sin entregar AL HACER EL ALBARÁN. Un pedido puede tener los dos (son dos
  fallos distintos). Tampoco con `NoServidoDeUnaVez`: las líneas con `Recoger` quedan en albarán/factura, así que ese no salta; este motivo
  es justo lo que aquél no ve. Ni con `IncidenciaDeAgenciaOReenvio`: el envío de la nota va con el número de la nota, no del pedido.

Referencia de los plazos: el día del pedido o, si la menor `Fecha Entrega` de sus líneas de producto es posterior (fecha pedida por el cliente), esa.
Decisiones (corte 1): modo 6 «Junto con lo pendiente del cliente» espera a propósito → no se evalúa el día siguiente; los modos 2/3/4 sí se evalúan
(esperar o partir es una consecuencia de que faltaba algo); «Recoge en tienda» se mide igual (el albarán sale de Algete). `DiasLaborablesHastaServir`
se da siempre que haya albarán (también en modo 6), cuente o no.

### Señales de calidad: dbo.CalidadEventos (NestoAPI#914, 26/11/26)
- Lo que se sale del pedido perfecto y hasta ahora solo salía por correo o push y se perdía. Tabla fuera del EDMX `Scripts/Issue914_CalidadEventos.sql`
  (Id, Fecha, Tipo, Empresa, Pedido, Cliente, Contacto, Documento, Usuario sin dominio, Imputado, Detalle JSON ≤ 1.000, Importe; índices (Empresa,
  Pedido) y (Tipo, Fecha); GRANT SELECT, INSERT). Sin ella: nada se apunta (ELMAH una vez) y los motivos que dependen de eventos, no evaluados.
- UN único punto: `RegistroCalidad.Apuntar(tipo, usuario, empresa, pedido, cliente, contacto, documento, imputado, detalle, importe)`
  (`Infraestructure/Calidad/RegistroCalidad.cs`): nunca lanza, nunca retrasa (segundo plano con `QueueBackgroundWorkItem`, conexión propia sin
  alistar y con `TransactionScope.Suppress`: no entra en la transacción de quien llama). `Importe` solo si es un coste que sale solo.
- Tipos (`TiposEventoCalidad`) y dónde se apuntan:

| Tipo | Dónde | Motivo |
|---|---|---|
| `CambioDeModoConPicking` | `PedidosVentaSolicitudesController.PostSolicitudCambioModo` (el correo «X pide cambiar el modo de entrega… ya tiene picking», #533), aunque el correo no salga. Detalle {ModoActual, ModoPedido, Comentario, CorreoEnviado}. | `PidioCambiarAlgoConPicking` |
| `QuitarPicking` | `ServicioQuitarPicking.Quitar` tras quitarlo (pedido entero o líneas). Usuario = quien lo quita (Almacén). Detalle {Lineas, PedidoEntero}. | `TocadoTrasElPicking` |
| `AmpliacionConPickingPedidoNuevo` | `ServicioAmpliacionPedidoEnPreparacion.SepararDelPut` (lo añadido se va a un pedido nuevo). Detalle {PedidoNuevo, Pickings, Productos}. | `PidioCambiarAlgoConPicking` |
| `AmpliacionConPickingMismoPedido` | `ServicioAmpliacionPedidoEnPreparacion.DespuesDeGuardar` con PedirAlmacen o AvisarAlmacen (se queda en el mismo pedido). | `TocadoTrasElPicking` |
| `RecogidaAnulada` | `ServicioRepartoPropio.AnularRecogida` (parada en reparto). Pedido = la nota NR; Documento «Envio N». | (ninguno: las notas no entran) |
| `RecogidaMarcadaPorError` | `ServicioRepartoPropio.AnularRecogidaMarcada` (caso 930553). Imputado: no se sabe aquí quién marcó «Recogido». | (ninguno) |
| `EnvioRutaPropiaFueraDeDia` | SIN CONECTAR (no hay un punto único donde se fuerce). | `EnvioDeRutaPropiaForzado` |
| `CobroDeMasConfirmado` | `ServicioRepartoPropio.Entregar`: efectivo con `ConfirmarCobroDeMas` = true, lo cobrado pasa del reembolso (`ReglaReembolsoEntero.EsDeMas`), no es nota de cobro y la entrega se registra NUEVA (el reintento con la misma clave no apunta). Usuario = el repartidor; Importe = lo cobrado de más; Documento «Envio N»; Detalle {Reembolso, Cobrado}. | (ninguno todavía) |
| `NoEstabaEnSuHueco` | `ServicioCambioHuecoPicking.ApuntarNoEstabaEnSuHueco` (EL punto único), desde `ServicioCambioHuecoPicking.Cambiar` (picking) y `ServicioCambioHuecoReposicion.Cambiar` (reposición) cuando el cambio de hueco de Ariadna#12 se hace (movidas > 0, nunca en ensayo). Usuario = el mozo; Empresa; sin pedido (un picking sirve a varios); Documento «Picking N» / «Traspaso N»; Detalle {Tipo, Producto, HuecoOrigen, HuecoDestino, Cantidad, Movidas}; Imputado vacío (quien lo ubicó no se sabe ahí). Hasta ahora no quedaba rastro (ni Modificaciones): lo que no estaba pasaba a «pendiente de ubicar» sin más. Conectado el 27/11/26 (corte 2). | (ninguno: para el corte 3, ubicaciones y cadena de custodia, y para «falta que sí estaba») |
| `CobroRepetidoConfirmado` | `ServicioCobrosReparto.Cobrar` (POST Cobros, con `CobrosRecientes`): con `Confirmar` = true se vuelve a mirar si se habría frenado (409 COBRO_REPETIDO o COBRO_TODO_A_CUENTA) y, si el cobro se contabiliza, se apunta. Usuario = el repartidor; sin pedido; Cliente/Contacto; Documento «Asiento N»; Importe = el cobro entero si es repetido, lo que va a cuenta si es todo a cuenta; Detalle {Motivo = el código}. Mirarlo es UNA lectura más solo en los confirmados; si falla, no se apunta y el cobro sigue. El cobro de una nota de cobro (dentro de la entrega) no se frena y no apunta. | (ninguno todavía) |

- Los dos cobros confirmados del reparto se conectaron el 26/11/26 (arriba). Candidatos vistos y NO conectados (habría que adivinar o no son del
  pedido perfecto): pedido retenido por falta (#858, ya en `PedidosRetenidosPorFalta`); entrega incompleta (#880, ya en
  `RepartoBultosPendientes`); aplazar líneas con picking (`AplazamientoPedidoConPicking`); cambiar el cliente con picking (#876, ya en Modificaciones).
  Pedir autorización de un pedido que no pasa la validación NO cuenta (Carlos 26/11/26).

### Almacén: preparación de pedidos, corte 2 (NestoAPI#914, 27/11/26)
- Solo lectura y en sombra, mismos permisos que el pedido perfecto (Dirección e Informática; 403 de usted). Aquí SÍ van nombres (es por preparador):
  antes de enseñárselo a nadie más, informar a los trabajadores (principio de la issue).
- **El preparador es el de Ariadna**: quien leyó el producto en el recorrido del picking (`dbo.PreparacionEscaneos`, TipoOrigen PICK, Fase PICK; Usuario
  sin dominio, #823: «NUEVAVISION\Alfredo» y «Alfredo» son la misma persona). Es fiable porque cada lectura la manda la PDA con el usuario del token
  (no se teclea) y con IdCliente único. NO se usa `EtiquetasPicking.UsuarioQuePrepara` (desde Nesto sale siempre el mismo usuario) ni la cabecera del
  picking de Nesto viejo. `PreparacionSalidasTerminadas.Usuario` (quién lo terminó) se lee, pero no manda.
- Pickings compartidos (pregunta abierta de la issue; decisión de este corte): cada PARADA (producto en un picking) es de quien leyó más unidades
  netas de ese producto (empate: la lectura más reciente; sin cogidas, quien dio la falta); cada línea, del de su parada. Un picking con paradas de
  dos personas cuenta para las dos y sale en `PickingsCompartidos`.
- Universo: los pickings (TipoOrigen PICK) cuya PRIMERA lectura del recorrido cae en [desde, hasta). Líneas = LinPedidoVta con ese Picking,
  TipoLinea 1, sin ficticios, Cantidad − Recoger > 0 (lo que se recoge, como el packing), agrupadas por pedido + producto. Una línea dada ENTERA por
  falta sale del picking (Picking = 0 al terminar) y solo queda su parada. Los pickings del rango de números del periodo sin ninguna lectura del
  recorrido (hechos en papel) no entran: salen contados en `PickingsSinRastro` (por almacén; estimación, los números son correlativos).
- `GET api/Calidad/Almacen/Resumen?empresa=1&desde=&hasta=` → {Empresa, Desde, Hasta, Total, Preparadores[], Indicadores[{Indicador, Texto, EsError,
  Ambito, EvaluadoDesde, Evaluadas, Con, Porcentaje}], IndicadoresNoEvaluados[{Indicador, PorQue, EvaluadoDesde, SinEvaluar}], SinRastro[{Indicador,
  Que, HaceFalta}], LineasSinPreparador, PickingsSinRastro{Pickings, Pedidos, Lineas, PorAlmacen}, PreparadorDesde, CalculadoEl, Aviso}. Total y cada
  preparador: {Preparador, Pickings, PickingsCompartidos, Pedidos, Lineas, Paradas, Unidades (cogidas netas), LineasConError, LineasEvaluadasError,
  PorcentajeError, Indicadores[{Indicador, Ambito, Evaluadas, Con, Porcentaje}]}. Ámbito Linea: sobre sus líneas evaluadas; Parada: sobre sus paradas
  evaluadas, y la falta se imputa a quien la dio; Lecturas: recuento sin denominador.
- `GET api/Calidad/Almacen/Detalle?empresa=1&preparador=&desde=&hasta=&indicador=&pagina=1&tamano=100` → {…, TotalLineas, TotalParadas,
  Lineas[{Picking, FechaPicking, Pedido, Cliente, Contacto, Producto, LineasPedido (Nº Orden), Cantidad, Almacen, Factura, Preparador, PackingEvaluado,
  Empaquetador, MetidasEnPacking, FaltasEnPacking, UnidadesRectificadas, Indicadores, NoEvaluados}], Paradas[{Picking, FechaPicking, Producto,
  Preparador, OtrosQueLeyeron, Cogidas, Faltas, DioLaFalta, CantidadEnLineas, MetidasEnPacking, FaltasEnPacking, Indicadores, NoEvaluados}],
  NoCasadas[{Picking, Usuario, Pantalla, Lecturas}]}. `preparador` opcional (sin dominio, sin distinguir mayúsculas); `indicador` = nombre del enum
  (un número o un nombre que no existe → 400 con la lista); con indicador solo salen las líneas/paradas que lo tienen (las paradas, imputadas a él).
- Periodo: por defecto de hoy − 3 meses a hoy (hoy no entra); como mucho 13 meses; hasta ≤ mañana. No hay rastro de Ariadna antes del 30/09/26 (#556).
- Código: `Infraestructure/Calidad/Almacen/`: LA REGLA (pura) `ReglaCalidadAlmacen.Evaluar(LecturaCalidadAlmacen)`, el enum `IndicadorAlmacen` con su
  atributo (Texto de usted, Ámbito, EsError), el lector `LectorCalidadAlmacenSql` (`LeerTanda` probado con resultados simulados) y `ServicioCalidadAlmacen`
  (caché 30 min por empresa + periodo, compartida por los dos GET; un fallo no se cachea; 503 + ELMAH). Controlador `CalidadAlmacenController`.
- Lector: SOLO LECTURA (READ UNCOMMITTED + NOLOCK), una ida por MES: #c (pickings con lecturas en la tanda, por IX_PreparacionEscaneos_Fecha) → #k
  (los que EMPIEZAN en la tanda) → todo lo demás por clave (IX_PreparacionEscaneos_Origen; LinPedidoVta por Empresa + Picking; rectificativas por
  factura + línea; no casadas por IX_LecturasNoCasadasAlmacen_Fecha). Nunca recorre LinPedidoVta ni ExtractoProducto por fechas. Tablas opcionales
  (OBJECT_ID): `PreparacionSalidasTerminadas`, `LecturasNoCasadasAlmacen`; sin `PreparacionEscaneos` no hay nada (Aviso).

#### Indicadores de almacén (definiciones exactas)
| Indicador | EsError | Ámbito | Cómo se calcula | Desde cuándo |
|---|---|---|---|---|
| `ErrorDeEnvioReclamado` (OFICIAL 1) | sí | Línea | `ReenviadoSinCoste` o `CambiadoPorOtro` en alguna de sus líneas (abajo). Es «el error real» de Carlos: lo que el cliente reclamó. Sobre las líneas servidas y ya evaluables (albarán de hace ≥ N días). No evaluado: sin albarán, servida hace menos de N días o sin preparador. | Primera lectura PICK |
| `ReenviadoSinCoste` | sí (parte del anterior) | Línea | Sub-motivo: se le volvió a mandar el mismo producto sin coste. | Como el anterior |
| `CambiadoPorOtro` | sí (parte del anterior) | Línea | Sub-motivo: rectificada y se le cargó o mandó sin coste otro producto parecido. | Como el anterior |
| `AbonadoSinReposicion` | NO | Línea | Sub-motivo informativo: rectificada sin reposición ni cambio (devolución o abono comercial). Solo si la línea NO tiene error. | Como el anterior |
| `FaltaEnPacking` (OFICIAL 2) | sí | Línea | El pedido se empaquetó con Ariadna en ese picking (alguna lectura PACK del pedido en ese picking) y las faltas netas del PACK de ese producto en ese pedido son > 0: el recorrido lo dio por cogido y en la mesa no apareció (la «falta de packing» de #556, que avisa a Compras; deshacerla = «ha aparecido»). Se imputa al preparador. | Primera lectura PACK (nunca antes de la primera PICK) |
| `DescuadrePickingPacking` | NO | Parada | Todos los pedidos del picking con ese producto se empaquetaron con Ariadna y (metidas en packing + faltas de packing) ≠ cogidas en el recorrido. Puede ser del preparador (leyó de más o de menos) o del empaquetador (cerró sin meterlo ni dar la falta). **Informativo, no se cuenta a nadie (decisión de Carlos 27/11/26).** | Como el anterior |
| `FaltaEnPicking` | NO | Parada | Faltas netas del recorrido (Metodo FALTA, con sus «deshacer») de ese producto en ese picking > 0. Imputada a quien la dio. Es falta de stock o de ubicación, no del mozo, salvo que el producto estuviera (eso es «FaltaQueSiEstaba», sin rastro). | Primera lectura PICK |
| `FaltaDeshechaPorOtro` | NO (casi-error) | Parada | Alguien tiene faltas netas > 0 del producto en el picking y OTRA persona faltas netas < 0 (quitó su falta: el producto sí estaba). Imputada a quien la dio. Deshacer la propia no cuenta. | Primera lectura PICK |
| `LineaRectificada` | NO | Línea | Alguna de sus líneas (`Nº Orden`) es `FacturaOriginalLinea` de `LinFacturaVtaRectificacion` (misma factura original, otra factura). No se sabe por qué (precio, cliente, devolución, producto equivocado…). Sin factura todavía: no evaluada. | Primera lectura PICK |
| `LecturaNoCasada` | NO (casi-error) | Lecturas | Filas de `LecturasNoCasadasAlmacen` (TipoOrigen PICK, de esos pickings) del usuario: Ariadna paró un código desconocido o de otro producto. No cuenta contra nadie (issue). | Primera fila de la tabla (Ariadna#43) |

- **Los dos indicadores OFICIALES por preparador (Carlos 27/11/26)**: (1) `ErrorDeEnvioReclamado` sobre sus líneas servidas y evaluables (el
  error real: lo que el cliente reclamó) y (2) `FaltaEnPacking` sobre sus líneas empaquetadas con Ariadna (el error que se cazó antes de salir).
  Cada uno con su denominador, en `Total`/`Preparadores[].ErrorDeEnvioReclamado` y `.FaltaEnPacking` ({Indicador, Ambito, Evaluadas, Con,
  Porcentaje}, la misma fila que en `Indicadores`); el resumen lleva `IndicadoresOficiales` = ["ErrorDeEnvioReclamado", "FaltaEnPacking"],
  `Indicadores[].Oficial` y `DiasReclamacionEnvio`. `FaltaDeshechaPorOtro` (la fiabilidad de las faltas que dio) y `DescuadrePickingPacking`,
  informativos. `LineasConError`/`PorcentajeError` se quedan (no se rompe el contrato) pero ahora son la UNIÓN de las líneas con algún error
  (FaltaEnPacking o ErrorDeEnvioReclamado) sobre las evaluadas para alguno: mezcla denominadores; para leer, los dos oficiales por separado.
- Detalle: cada línea lleva `SubMotivosReclamacion` (aunque aún no cuente), `ReclamacionEvaluada` y `PruebasReclamacion[{SubMotivo, Pedido,
  Linea, Fecha, Producto, Cantidad, Importe, FacturaRectificativa, EnRectificativa, NotaEntrega}]`: la línea posterior que lo demuestra.
- Sin rastro (salen en `SinRastro` con lo que haría falta): `FaltaQueSiEstaba` (las lecturas del recorrido no guardan el hueco de lo cogido; haría
  falta guardarlo y apuntar cuando un recuento del hueco encuentre lo dado por falta; `NoEstabaEnSuHueco` es la otra cara y ya se apunta),
  `EtiquetaConPackingIncompleto` (no se mira el packing al imprimir la etiqueta; conectar es una lectura más en `ServicioEtiquetaPacking.Imprimir`).
  `ProductoOCantidadEquivocados` y `ReenvioPorPreparacion` salieron de `SinRastro` el 27/11/26: los mide `ErrorDeEnvioReclamado`.

#### Error de envío reclamado (NestoAPI#914, Carlos 27/11/26)
«El error real es cuando decimos que hemos mandado un producto y en realidad hemos mandado otro (o ninguno). ¿Cómo nos damos cuenta? Porque el
cliente protesta. ¿Cómo se podría ver en los datos? Mirando si después del pedido hay una rectificativa o se vuelve a enviar el mismo producto sin
coste.» Código: `Infraestructure/Calidad/Reclamaciones/` (LA REGLA pura `ReglaErrorEnvioReclamado.Evaluar`, y la lectura común
`LectorReclamacionesEnvioSql.SQL_RECLAMACIONES`, que se pega al lote de cada lector partiendo de su `#s0 (Empresa, Pedido, Linea)`: en Almacén,
las líneas de los pickings; en el pedido perfecto, las líneas servidas de los pedidos del mes). Por LÍNEA servida (con albarán), mirando desde
su Fecha Albarán hasta N días después; N = parámetro «(defecto)» `Calidad.DiasReclamacionEnvio` (1-365; sin fila o mal, 45; se lee con un
SELECT de la fila «(defecto)», no crea filas). No evaluada: sin albarán o servida hace menos de N días (el cliente aún puede reclamar; si ya se ve
algo, sale en el detalle pero no cuenta). En Almacén, además, sin preparador de Ariadna.
- **`ReenviadoSinCoste`**: el mismo cliente recibe el MISMO producto en otra línea de producto (otro pedido o nota de entrega; estado ≠ −3) con
  Cantidad > 0 y `Base Imponible` = 0, en un pedido cuya `Fecha` cae en [Fecha Albarán, Fecha Albarán + N]. Falsos positivos cerrados: la línea
  original ya iba gratis (base 0), era muestra (MMP) o parte de una oferta; la posterior es muestra/material promocional (SubGrupo MMP), lleva
  `NºOferta` (la gratis de un N+M o un regalo de oferta), es un producto de `dbo.Ganavisiones` o de `dbo.RegaloImportePedido` vigente ese día,
  es del MISMO pedido o es la nota de lo pendiente de ESE pedido (#542, `PedidoOrigen` = el pedido). Cada línea posterior demuestra UNA
  reclamación: la de la última vez que se le sirvió ese producto antes (quien lo compra dos veces en la ventana no cuenta doble).
- **`CambiadoPorOtro`**: la línea está rectificada (`LinFacturaVtaRectificacion` por FacturaOriginalNumero + FacturaOriginalLinea, otra factura)
  y se le carga o manda sin coste OTRO producto «parecido» = misma `Familia` (marca) o mismo `Grupo` + `SubGrupo` (nunca MMP), que no sea un
  regalo (como arriba), y: (a) en la MISMA rectificativa (sus líneas por `Nº Factura`, cargado o sin coste), salvo que la rectificativa también
  abone ese otro producto (eso es una refacturación de precio o de cliente, no un cambio); o (b) SIN COSTE en otro pedido del mismo cliente con
  Fecha a ±7 días de la rectificativa y no antes del albarán. Decisión: lo CARGADO en un pedido normal no cuenta (con quien compra cada semana la
  regla «misma familia o subgrupo» sería demasiado laxa); por eso no hizo falta el «precio de tarifa parecido».
- **`AbonadoSinReposicion`** (informativo, NO es del preparador): rectificada y ni reenvío ni cambio (devolución, abono comercial, precio).
- `ErrorDeEnvioReclamado` = `ReenviadoSinCoste` o `CambiadoPorOtro`.
- Falsos positivos que quedan: un regalo comercial del vendedor al 100 % del mismo producto que no lleve oferta ni sea Ganavisiones (sale como
  reenvío); un producto que esté en `dbo.Ganavisiones` o `RegaloImportePedido` y SÍ se reenvió por error (sale como regalo: falso negativo);
  en `CambiadoPorOtro` (b), un regalo sin oferta de un producto de la misma marca cerca de una rectificativa por otra causa. Un reenvío con coste
  (se le vuelve a cobrar) no se ve. Lo que el cliente reclamó y se arregló sin documento (le llevó el comercial el producto) tampoco.
- Lectura (SOLO LECTURA, NOLOCK, en el mismo lote de cada tanda): las servidas por PK de LinPedidoVta; lo posterior del mismo producto por
  LinPedidoVta Empresa + Nº Cliente + Producto (como `RegistroOfertasMostradas`) y su CabPedidoVta por PK; las rectificaciones por factura +
  línea; las líneas de sus rectificativas por Nº Factura; y lo sin coste del cliente alrededor de la rectificativa por CabPedidoVta Empresa +
  Nº Cliente + Fecha (solo de los clientes con líneas rectificadas). Nunca LinPedidoVta por fechas a pelo.
- NO imputables a preparación (decisión de este corte, se quedan en el pedido perfecto): `EntregaIncompleta` (#542: las líneas con `Recoger` se deciden
  antes del picking, que ya no las recoge) y `TocadoTrasElPicking` (lo hace Almacén a petición de otro).
