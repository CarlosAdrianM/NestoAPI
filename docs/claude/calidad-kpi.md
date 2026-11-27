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
| `EtiquetaConPackingIncompleto` | `ServicioEtiquetaPacking.Imprimir` (POST api/Almacen/Pedidos/{pedido}/Etiqueta), al grabar el envío de la etiqueta con productos sin leer en la mesa (`ReglaPackingIncompleto`): el mozo confirmó tras el 409 PACKING_INCOMPLETO (`Modo` «Confirmado») o la Ariadna no avisa (anterior a 2.48, «SinAviso»). Una vez por envío. Usuario = el empaquetador; Empresa, Pedido, Cliente/Contacto del envío; Documento «Envio N»; Detalle {Modo, Picking, Unidades, Productos[{Pedido, Producto, Cantidad}]}. Conectado el 27/11/26. | (ninguno: indicador informativo del empaquetador en el resumen de Almacén) |
| `CobroRepetidoConfirmado` | `ServicioCobrosReparto.Cobrar` (POST Cobros, con `CobrosRecientes`): con `Confirmar` = true se vuelve a mirar si se habría frenado (409 COBRO_REPETIDO o COBRO_TODO_A_CUENTA) y, si el cobro se contabiliza, se apunta. Usuario = el repartidor; sin pedido; Cliente/Contacto; Documento «Asiento N»; Importe = el cobro entero si es repetido, lo que va a cuenta si es todo a cuenta; Detalle {Motivo = el código}. Mirarlo es UNA lectura más solo en los confirmados; si falla, no se apunta y el cobro sigue. El cobro de una nota de cobro (dentro de la entrega) no se frena y no apunta. | (ninguno todavía) |

- Los dos cobros confirmados del reparto se conectaron el 26/11/26 (arriba). Candidatos vistos y NO conectados (habría que adivinar o no son del
  pedido perfecto): pedido retenido por falta (#858, ya en `PedidosRetenidosPorFalta`); entrega incompleta (#880, ya en
  `RepartoBultosPendientes`); aplazar líneas con picking (`AplazamientoPedidoConPicking`); cambiar el cliente con picking (#876, ya en Modificaciones).
  Pedir autorización de un pedido que no pasa la validación NO cuenta (Carlos 26/11/26).

### Encuestas a clientes, corte E1 en sombra (#914, 27/11/26)
Especificación: el último comentario de la issue («Corte nuevo: la voz del cliente»). E1 NO envía NADA (ni correo, ni push, ni WhatsApp) ni tiene
plantillas ni página de respuesta (eso es E2). Código en `Infraestructure/Calidad/Encuestas/`.

- **El gobernador** (`GobernadorEncuestas`, PURO, con tests): historial del cliente + su situación + las candidatas de un día → como mucho UNA y, de
  las demás, por qué no (`MotivoDescarteEncuesta`, el primero que se cumple, en este orden): `Baja` → `Impago` → `TopeEntreEncuestas` → `TemaRepetido`
  → `SinCanal`; las que pasan y no ganan, `OtraCandidataElegida`.
  - Intervalo: el que se le PASA (la regla no lee parámetros). Suelo `ParametrosEncuestas.DIAS_ENTRE_MINIMO` = 30 (Carlos 27/11/26: se puede bajar a
    45 o 30 para recoger datos, NUNCA menos): `ConSuelo`. Si las DOS últimas encuestas fueron ignoradas, ×2; con cada ignorada más, ×2 otra vez
    (sobre el intervalo que toque: con 30, 60, 120…); tope 365. Ignorada (`ReglaEncuestaIgnorada`) = enviada y 14 días sin respuesta: en sombra no
    hay ninguna.
  - Tema repetido: la última fue de esa clave, o hubo una de esa clave hace menos de 12 meses. Clave (`CandidataEncuesta.ClaveTema`) = el tema y, en el
    de Vendedor, «Vendedor|código»: si le cambian el vendedor se le puede preguntar por el nuevo (el tope general sigue mandando). Decisión de este
    corte: «dos seguidas» también se mira por vendedor.
  - Prioridad (`ClaseCandidataEncuesta`): ProblemaResuelto > Cambio > PrimeraVez > Otra; a igualdad, el TEMA por el que hace más que no se le pregunta
    (nunca = el que más); después el evento más reciente; después un orden fijo (tema, fuente, pedido, vendedor).
- **Parámetros** (ParametrosUsuario empresa 1, «(defecto)», leídos con `LeerParametroSinDefecto`: no crean fila): `Calidad.EncuestasDiasEntre` (60
  sin fila; < 30 → 30, > 180 → 180, ilegible → 60, y la respuesta lo dice en `Aviso`), `Calidad.EncuestasSombra` (solo «1» enciende el job; nace
  apagado), `Calidad.EncuestasDiasTrasAparato` (15), `Calidad.EncuestasDiasTrasFormacion` (7).
- **Bajas**: la baja de TODO de `CorreosPostCompraBajas` (#532, Tipo NULL; por cliente o por cualquiera de sus correos), con la misma lectura que el
  recordatorio de reposición (`RepositorioRecordatorioReposicionSql.SQL_BAJAS` + `ConstruirBajas`). Las bajas por tipo (vídeos, reposición) y las de
  push de `NotificacionesClientesBajas` (#692: solo EventosYCursos y AvisosReposicion) NO cuentan: son de otra cosa. Sin la tabla o si falla, no se
  mira (y sale en `Aviso`).
- **Impago en gestión**: un impagado SIN LIQUIDAR: `ExtractoCliente` TipoApunte 4, Importe > 0, Concepto que no empieza por «Gastos Impagado» (como
  `ModeloImpagados`) e `ImportePdte` ≠ 0; cuenta desde la fecha del más antiguo.
- **Clientes que nunca**: `ReglaClientesEncuesta` = los de factura simplificada (tienda online 31517, Amazon 32624, público final 10458, materiales de
  cursos 31794, pedido tienda 9500) y El Edén (15191).
- **Canal previsto** (`ReglaCanalEncuesta`, solo se calcula): push si el cliente tiene la app con avisos (`FuentePushCampanaSql.FiltroDestinatarios`,
  el de la push de campaña); si no, correo: el primero válido (`ComercialesPedido.EsCorreoValido`) de las personas de contacto activas (Estado ≥ 0)
  de ESE contacto o, si no tiene, de otro contacto del cliente; si no, `SinCanal`. NO se exige «Enviar boletín» (no es un correo comercial; duda
  abierta para Carlos).

#### Candidatas por evento (`IFuenteCandidatosEncuesta`, una clase por evento; `FuentesCandidatosEncuesta.Todas()`)
Cada fuente lee con NOLOCK, en UNA consulta, la ventana de FECHAS DEL EVENTO = la de días de candidatura menos su retraso; el día de la candidata =
fecha del evento + retraso. Cada candidata lleva cliente, contacto, tema, clase, motivo de usted, fecha del evento y enlaces (pedido, factura, envío,
evento, producto, vendedor + nombre + ámbito, profesora).

| Fuente | Tema / clase | Evento (de dónde) | Retraso |
|---|---|---|---|
| `EntregaPedido` | Entrega / Otra | `EnviosAgencia` Estado 2 + `FechaEntrega` (la real en ese estado: la pone el seguimiento de GLS, Innovatrans y CTT y el repartidor de la 14 en Ariadna). Sin notas, rectificativas ni copias; con alguna línea de producto servida. | 2 |
| `VendedorPrimerPedido` | Vendedor / PrimeraVez | Primer pedido del cliente con ESE vendedor por ámbito, con LA REGLA de comisiones y factura (`ComercialesPedido.CodigosVendedores`, sin dar la peluquería huérfana al de estética): líneas PEL → `VendedoresPedidoGrupoProducto` PEL; el resto → `CabPedidoVta.Vendedor`. Un pedido mixto da DOS. | 3 |
| `VendedorCambioFicha` | Vendedor / Cambio | `LogModificaciones` (trgClientesUpd: Tabla Clientes, Columna Vendedor, ValorAntiguo); el nuevo = el que tiene HOY la ficha. | 14 |
| `VendedorPeluqueriaCambioFicha` | Vendedor / Cambio | `VendedoresClienteGrupoProducto` PEL NO deja rastro: su `FechaModificacion` y un vendedor distinto del de peluquería del último pedido ANTERIOR del cliente. Sin pedido PEL anterior, no sale. | 14 |
| `ProblemaNotaEntregaPendiente` | Problema / ProblemaResuelto | La nota de lo pendiente (`NotaEntrega` = 1 con `PedidoOrigen`, #542) entregada (su envío en Estado 2). | 2 |
| `ProblemaReenvioEntregado` | Problema | `EnviosAgenciaReenvios` «Reenviar» con el reenvío entregado. Opcional (tabla). | 2 |
| `ProblemaBultosPendientesCerrados` | Problema | `RepartoBultosPendientes` (#880) con TODOS los bultos del envío cerrados; el último `FechaCierre`. Opcional. | 2 |
| `ProblemaRectificativa` | Problema | `CabFacturaVta` con filas en `LinFacturaVtaRectificacion` (la misma fuente que FacturaRectificadaEntera/EnParte). | 3 |
| `CobroPrimerGiro` | Cobro / PrimeraVez | Primer recibo del cliente en una remesa (`ExtractoCliente` TipoApunte 2 con Remesa; el día, `Remesas.Fecha`, como ModeloImpagados) sin otro en una remesa anterior. | 3 |
| `TecnicaAparato` | Información técnica / Otra | Pedido entregado (como `EntregaPedido`) con una línea del grupo «APA» (`Constantes.Productos.GRUPO_APARATOS`, el de las reglas de muestras y de ruta; `NecesitaNumSerie` es solo un subconjunto). Carlos: las profesoras intervienen sobre todo con aparatos y cursos, y la puesta en marcha es después. | `DiasTrasAparato` (15) |
| `TecnicaFormacionSinEvento` | Información técnica | Línea CUR facturada (`[Fecha Factura]`, Estado ≥ 4) que es formación sin evento (`ReglasFormacionPlantilla.EsFormacion` y no el `ProductoCurso` de ningún evento). | `DiasTrasFormacion` (7) |
| `TecnicaAsistenciaEvento` | Información técnica | `EventosInscripciones` Estado «Asistio» (#908; de pago o gratuito); profesora = `Eventos.Ponente`. Los cursos CON evento solo salen por aquí (si asistió). | 2 |

- Fuera por falta de datos: el CAMBIO DE FORMA DE PAGO de la ficha (`CondPagoCliente` no tiene fecha ni el disparador lo apunta); las incidencias de
  agencia que acabaron entregadas sin reenvío (el seguimiento no guarda la historia del estado); la entrega de lo que no va por agencia (recoge en
  tienda, agencias sin seguimiento, que nunca llegan a Estado 2); quién hizo la puesta en marcha o la formación de un aparato (no consta: haría falta
  apuntarlo, p. ej. un rapport de la profesora con el pedido); el pedido de una inscripción a un evento.
- Vendedores que no son una persona (`ReglaVendedorEncuesta.EsPersona`): el general «NV» (`ComercialesPedido.EsComercialReal`), Estado 99 (para
  anular) y Estado negativo. Nombre visible = `Vendedores.Descripción` (el del correo del pedido). Ámbito `Estetica`/`Peluqueria`
  (`AmbitosVendedorEncuesta.Texto`: «de estética»/«de peluquería», SIN género: Vendedores no lo tiene; E2 tendrá que redactar «la atención de X, que
  le atiende en …»). El vendedor guardado es al que se avisará de una nota mala (E3).

#### Tablas (`Scripts/Issue914e_CalidadEncuestas.sql`, fuera del EDMX, como sa; idempotente)
- `dbo.CalidadEncuestas`: una fila por encuesta decidida (UNIQUE Empresa + Cliente + Fecha), con todo lo de la candidata, `CanalPrevisto`, `Correo`,
  `IntervaloDias`, `Sombra` = 1 y las columnas de E2 vacías (`EnviadaEl`, `CanalEnvio`, `RespondidaEl`, `Nota` 1-3, `Texto`). GRANT SELECT, INSERT, UPDATE.
- `dbo.CalidadEncuestasDescartes` (las candidatas no cogidas y el motivo) y `dbo.CalidadEncuestasDias` (día procesado: lo que hace el job idempotente).
  GRANT SELECT, INSERT.
- Sin el script: la simulación funciona (no escribe), el job no hace nada (ELMAH una vez) y `…/Sombra` da 503 (ELMAH una vez).

#### Job y endpoints
- Job `calidad-encuestas-sombra` (todos los días 4:45, cola «indicadores»): con `Calidad.EncuestasSombra` = 1 y las tablas, las candidatas de AYER →
  gobernador con el historial GUARDADO (sombra incluida, 800 días) → guarda en una transacción encuestas, descartes y el día. Día ya procesado: nada.
  Nunca lanza (ELMAH).
- `GET api/Calidad/Encuestas/Simulacion?desde=&hasta=&diasEntre=&comparar=` (Dirección e Informática, 403 de usted; `CalidadPedidoPerfectoController.PuedeVer`).
  Días de candidatura [desde, hasta): por defecto los 90 hasta hoy; como mucho 180; hasta ≤ hoy. `diasEntre` 30-180 (si no, 400); sin él, el del
  parámetro. NO escribe: recorre día a día con el historial EN MEMORIA desde vacío (nadie ignora). Lee las candidatas UNA vez (caché 30 min por
  rango y retrasos) y con `comparar=30,45,60` (hasta 5, cada uno 30-180) aplica el gobernador una vez por intervalo →
  `ComparacionSimulacionEncuestasDTO` {Empresa, Desde, Hasta, Candidatas, Comparacion[{DiasEntre, Encuestas, MediaPorDia, MaximoPorDia,
  ClientesEncuestados, ClientesCon1, ClientesCon2, ClientesCon3oMas, MaximoEncuestasMismoCliente, PerdidasPorTope, PorTema}], Simulaciones[], Aviso,
  CalculadoEl}. Sin `comparar` → `SimulacionEncuestasDTO` {Empresa, Desde, Hasta, DiasEntre, Dias, Candidatas, Encuestas, MediaPorDia, MaximoPorDia,
  DiaDelMaximo, ClientesConCandidatas, ClientesEncuestados, ClientesPorNumeroDeEncuestas[{«1»|«2»|«3 o más», Clientes}], MaximoEncuestasMismoCliente,
  PerdidasPorTope, PorTema[{Tema, Candidatas, Encuestas}], PorCanal[{Canal, Encuestas}], PorOrigen[{Origen, Tema, Candidatas, Encuestas}],
  Descartes[{Motivo, Candidatas}], PorDia[{Dia, Candidatas, Encuestas}], MasDescartadosPorTope (20: {Cliente, Candidatas, DescartadasPorTope,
  Encuestas}), Aviso, CalculadoEl}. Límite: canal, bajas e impagos son los de HOY (un impagado ya cobrado no se ve); lo dice el `Aviso`. Fallo → 503.
- `GET api/Calidad/Encuestas/Sombra?dias=30` (1-365): lo guardado por el job en [hoy − dias, hoy), con el mismo `SimulacionEncuestasDTO`
  (`DiasEntre` = el del último día procesado). 503 sin el script.
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
| `FaltaQueSiEstaba` | NO | Parada | (27/11/26, con el hueco de cada lectura) Versión SIMPLE (`ReglaFaltaQueSiEstaba`, pura): una falta neta del recorrido (Metodo FALTA, con sus deshacer) de ese producto en ese picking, en un hueco (columna `Hueco` o, si no, el `Motivo` de la falta), y en las 4 horas siguientes OTRA salida (otro picking o una reposición; la misma no, eso es `FaltaDeshechaPorOtro`) coge ese producto de ESE MISMO hueco (`Hueco` de la cogida, comparado sin barras). Lo de «sin que entre stock en el hueco entre medias» no se puede saber (las ubicaciones no guardan cuándo entró cada unidad): por eso la ventana corta. Imputada a quien dio la falta. Detalle de la parada: `CogidasDespuesDeLaFalta`. Límite (Ariadna 2.48): en las salidas que no cambian la reserva, si el mozo lo coge de otro hueco la app manda el de la parada; puede acusar en falso, por eso es informativo y con ventana corta (mejor quedarse corto). Sin la columna: no evaluado y sigue en `SinRastro`. | La primera lectura con hueco del periodo (y nunca sin la columna) |
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
- Sin rastro (salen en `SinRastro` con lo que haría falta): `FaltaQueSiEstaba` SOLO mientras no exista `PreparacionEscaneos.Hueco`
  (`Scripts/Issue914c_PreparacionEscaneosHueco.sql`; con la columna sale de `SinRastro` y se mide, ver la tabla; `NoEstabaEnSuHueco` es la otra cara),
  `ProductoOCantidadEquivocados` y `ReenvioPorPreparacion` salieron de `SinRastro` el 27/11/26: los mide `ErrorDeEnvioReclamado`.
  `EtiquetaConPackingIncompleto` también (27/11/26): se apunta al grabar el envío de la etiqueta y sale en el resumen.
- **Etiquetas con el packing incompleto (27/11/26, Carlos: «avisar y dejar seguir»)**: indicador INFORMATIVO del EMPAQUETADOR, no del preparador.
  `Resumen.EtiquetasConPackingIncompleto` = {EvaluadoDesde (primer evento), Total, PorEmpaquetador[{Empaquetador, Etiquetas, Confirmadas,
  SinAviso}]}: recuento de eventos `EtiquetaConPackingIncompleto` de `dbo.CalidadEventos` por usuario (sin dominio), con la FECHA DEL EVENTO en el
  periodo (no la del picking), por (Tipo, Fecha). Sin la tabla: vacío y en el Aviso. Cómo se decide «incompleto»: `docs/claude/almacen-etiquetas.md`.

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
### Tablero de KPI con nota semanal (#914, 27/11/26)
Idea de Carlos (como los informes de Amazon): cada KPI tiene su valor «ahora» y una nota por semana (POOR, FAIR, GOOD, FANTASTIC) para ver la
evolución. Cada persona ve SOLO los suyos; Dirección e Informática, los de todos en una vista aparte. Este corte es solo la API; las pantallas
(fondo de Nesto, franja de NestoApp) vienen después, con issue gemela cuando el contrato esté cerrado. Antes de encenderlo en pantallas, informar
a los trabajadores (ya dicho en la épica).

- Código: `Infraestructure/Calidad/Tablero/`. `IKpiTablero` (una clase por KPI, `KpisTablero.cs`; catálogo `CatalogoKpiTablero.Todos`): Clave
  estable, Nombre, Descripcion (de usted), TiposDueno (`Empresa`, `Almacen`, `Vendedor`, `Preparador`), Unidad (Porcentaje, Euros, Unidades),
  Sentido (MasEsMejor/MenosEsMejor), DiasMaduracion, PorQueMadura, TieneFuente y `Calcular(PeriodoKpi, IFuentesKpi)` → [{TipoDueno, Dueno, Valor,
  Numerador, Denominador, Detalle}]. Las fuentes (`FuentesKpiSql`) NO tienen consultas paralelas: reutilizan la de #893, el pedido perfecto y las
  filas de #634/#906; memoria por instancia (dos KPI que leen lo mismo, una lectura) y los pedidos por MESES naturales enteros.
- Semana = lunes a domingo. Cerrada el lunes siguiente + DiasMaduracion (`SemanaKpi.CerradaEl`).

#### Catálogo (definiciones exactas)
| Clave | Dueño | Unidad / sentido | Maduración | Definición |
|---|---|---|---|---|
| `PerdidaDesconocida` | Almacén (ALG, REI, ALC) | € a coste, menos es mejor | 0 | La consulta `GestorPerdidaDesconocida.SQL_PERDIDA_DESCONOCIDA` (#893: ExtractoProducto 1+3, `_RegInvent` y `_ErrRepo`) de [lunes, lunes+7) y `GestorPerdidaDesconocida.Construir` con la semana como periodo ÚNICO (todas las filas al «mes 1», para que no la parta un cambio de mes o de año). Valor = `PerdidaRealValor` del acumulado (falta que no cuadra con sobra del mismo producto en otro almacén); Numerador = `PerdidaRealUnidades`. Sin porcentaje: #893 no da una base de lo vendido o movido. |
| `DiferenciasReposicion` | Almacén o tienda imputado | € a coste, menos es mejor | 7 | Diferencias de `DiferenciasReposiciones` con FechaRecepcion en la semana, imputadas a QUIEN PIERDE (`ReglaQuienPierdeReposicion`): Confirmada/NoEstaba → origen y ErrorRecepcion/Sobraba → destino (`ReglaIndicadorErrores.ErroresDe`); Desacuerdo → lo que decidió `ReglaImputacionDesacuerdo` unidad a unidad (`DiferenciasReposicionImputaciones.AlmacenImputado`). Pendientes (o sin origen): sin imputar, en el Detalle. Valor = unidades imputadas × `Productos.PrecioMedio` de hoy (faltas y sobras); Numerador = unidades. Sin la tabla de #634: nada. |
| `DiferenciasInventario` | Almacén | € a coste, menos es mejor | 0 | Las filas `_RegInvent` de la MISMA consulta de #893 en la semana (la fecha del inventario contabilizado). Por producto, altas y bajas SIN netear entre sí: Valor = Σ (altas + bajas) × coste; Numerador = productos descuadrados («líneas»; un producto contado dos veces en la semana cuenta una). |
| `EntregasIncompletas` | Empresa («1») y almacén de salida | %, menos es mejor | 3 | Pedidos del universo del pedido perfecto con PRIMER albarán en la semana (buscados entre los metidos hasta 28 días antes del lunes; lo más viejo se escapa). Incompleto = `TieneNotaEntregaPendiente` (#542, motivo EntregaIncompleta) o `EntregaIncompleta` del reparto (#880, motivo EntregaIncompletaDelReparto, desde 19/11/26). Valor = incompletos / servidos; almacén = el de las líneas (`HechosPedidoPerfecto.Almacen`, ALG si vacío). Madura 3 días: lo servido el viernes se entrega el lunes o el martes. |
| `PedidoPerfecto` | Vendedor (`CabPedidoVta.Vendedor`) y empresa | %, más es mejor | 21 (`ReglaPedidoPerfecto.DIAS_PLAZO_SIN_SERVIR`, el plazo más largo de la regla) | Pedidos METIDOS en la semana (universo y regla del corte 1), perfectos SIN contar los informativos (`PerfectoSinContarInformativos`: «no se cobró al contado» y «tiene copia» no son culpa de nadie). Detalle = el motivo de error con más pedidos («Lo que más resta: NoSalioAlDiaSiguiente (2 de 4 pedidos)…»). Un impagado que llega cuando la semana ya no se recalcula (más de 8 semanas cerradas) no la cambia. |
| `ReenviadoSinCoste` | Preparador | € | 14 (provisional) | SIN FUENTE: el indicador `ErrorDeEnvioReclamado` del corte de almacén se programa en otra rama. La clase está registrada, devuelve vacío, el job no la calcula y el tablero la lista como «todavía no disponible». TODO en la clase: el lector a enchufar. |

#### Tablas (fuera del EDMX; `Scripts/Issue914t_CalidadTablero.sql`, idempotente, como sa)
- `dbo.CalidadKpiSemanas` (Id, Kpi, TipoDueno, Dueno, Semana date = lunes (CHECK), Valor, Numerador, Denominador decimal(19,4), Detalle nvarchar(400),
  CalculadoEl; ÚNICA Kpi+TipoDueno+Dueno+Semana). Cada semana de un KPI se sustituye entera (DELETE + INSERT en transacción). GRANT SELECT, INSERT, DELETE.
- `dbo.CalidadKpiUmbrales` (Id, Kpi, TipoDueno, CortePoorFair, CorteFairGood, CorteGoodFantastic, VigenteDesde (lunes), Usuario, FechaModificacion;
  ÚNICA Kpi+TipoDueno+VigenteDesde). GRANT SELECT, INSERT, UPDATE.
- Sin el script: los endpoints dan 503 (de usted, con el nombre del script) y el job no hace nada (ELMAH una vez).

#### Nota y tendencia (LA REGLA, `ReglaNotaKpi`, pura)
- Más es mejor: valor < PoorFair → POOR; < FairGood → FAIR; < GoodFantastic → GOOD; si no, FANTASTIC (el corte empieza la nota de arriba). Menos es
  mejor: los cortes van de mayor a menor y > PoorFair → POOR, > FairGood → FAIR, > GoodFantastic → GOOD; si no, FANTASTIC. Sin umbrales o sin valor:
  null («sin nota»).
- La nota NO se guarda: se saca con el umbral VIGENTE ESA SEMANA (VigenteDesde ≤ lunes, el más reciente). Por eso un cambio de umbral no reescribe las
  notas pasadas.
- Tendencia (Sube/Baja/Igual) del VALOR de la última semana cerrada frente a la media de las 4 anteriores con valor; Igual si cambia ≤ 2 % de la media
  (con media 0, solo si sigue en 0). `Mejora` = si es a mejor según el sentido.

#### Valor en curso
- Solo los KPI con maduración 0 (hoy: pérdida desconocida e inventario): [lunes, mañana), caché en memoria de 15 min por KPI (y las fuentes de la
  semana compartidas esos 15 min: los dos leen la misma consulta), marcado `EnCurso` y con `NotaProvisional` (nunca nota definitiva). Si falla: null y ELMAH.
- Con maduración > 0: sin valor en curso; `SemanaActualDisponibleEl` dice cuándo se podrá cerrar la semana actual.

#### Job y recalcular
- `calidad-tablero-kpi` (`TableroKpiJobsService.CalcularSemanal`), los LUNES a las 4:20 Madrid, cola «indicadores». Por KPI con fuente: de sus últimas 12
  semanas CERRADAS, las que no están guardadas y SIEMPRE las 8 más recientes (datos tardíos: resoluciones de diferencias, impagados, entregas del
  reparto). Si por retraso de la cola arrancase en horario (L-V 8-20), se reprograma a las 21:00. Un fallo en una semana no para el resto (ELMAH).
- `POST api/Calidad/Tablero/Recalcular?semanas=12[&ahora=true]` (Dirección/Informática; 1-104): recalcula TODAS las semanas cerradas de las últimas N.
  Se ENCOLA (202 {JobId, Programado, Semanas, Mensaje}); de L a V de 8 a 20 se programa para las 21:00 salvo `ahora=true` (como el entrenamiento de impagados).

#### «Quién soy» (`ResolutorIdentidadTablero`)
- Vendedor: `ServicioVendedoresVisibles.VendedoresDelUsuario` (parámetro de usuario «Vendedor», claim «Vendedor» de NestoApp y UsuarioVendedor), como
  «clientes para contactar».
- Almacén: `AlmacénPedidoVta` (ParametrosUsuario) SOLO para los grupos Almacén (sin él, ALG) y Tiendas (sin él, ninguno). Un vendedor con
  AlmacénPedidoVta no es dueño de un almacén.
- Preparador: el usuario sin dominio (el de Ariadna), si es de Almacén o Tiendas.
- Empresa: Dirección. Informática ve todo en la vista de Dirección pero no tiene «suyos» de empresa.
- Se quedan sin KPI (lista vacía): Administración, Compras, Facturación, Informática sin vendedor, Tiendas sin AlmacénPedidoVta, clientes de la tienda.

#### Endpoints (`CalidadTableroController`, `[Authorize]`)
- `GET api/Calidad/Tablero/Mios` (cualquier autenticado) → [{Kpi, Nombre, Descripcion, TipoDueno, Dueno, Unidad, Sentido, Disponible, DiasMaduracion,
  EnCurso {Semana, EnCurso, HastaDia, Valor, Numerador, Denominador, Detalle, NotaProvisional, CalculadoEl} | null, SemanaActualDisponibleEl,
  UltimaSemana {Semana, Valor, Numerador, Denominador, Detalle, Nota, CalculadoEl}, Tendencia, Mejora, Ultimas [{Semana, Valor, Nota}] (8, de la más
  antigua a la más nueva), TieneUmbrales, Aviso}]. Nunca los de otro; sin ninguno, [] (200).
- `GET api/Calidad/Tablero/{kpi}/Semanas?semanas=12[&tipoDueno=&dueno=]` → {Kpi, Nombre, Unidad, Sentido, TipoDueno, Dueno, DiasMaduracion,
  PorQueMadura, Disponible, Semanas [SemanaKpiDTO] (de la más nueva a la más antigua), EnCurso, SemanaActualDisponibleEl, Umbrales (vigentes), Aviso}.
  Sin `dueno`, el propio (sin ninguno: Dueno null, Semanas [] y Aviso «Usted no tiene este indicador»); con un `dueno` que no es suyo, solo
  Dirección/Informática (403 de usted). KPI que no existe: 404; tipo que no es del KPI o semanas fuera de 1-104: 400.
- `GET api/Calidad/Tablero?semana=` (Dirección/Informática; sin semana, la pasada; una que no ha empezado, 400) → {Semana, Kpis [{Kpi, Nombre, Unidad,
  Sentido, Disponible, Cerrada, EnCurso, DisponibleEl, Valores [{TipoDueno, Dueno, Valor, Numerador, Denominador, Detalle, Nota, NotaProvisional}], Aviso}]}.
- `GET api/Calidad/Tablero/Umbrales/Propuesta?kpi=[&tipoDueno=]` (Dirección/Informática) → {Kpi, Nombre, Unidad, Sentido, Tipos [{TipoDueno,
  SemanasDueno, Desde, Hasta, Propuesta {CortePoorFair, CorteFairGood, CorteGoodFantastic} | null, Distribucion {POOR, FAIR, GOOD, FANTASTIC},
  Vigentes, DistribucionVigentes, Aviso}]}. Propuesta = percentiles 25, 50 y 75 (interpolados, como PERCENTIL.INC) de todas las semanas-dueño
  guardadas de ese tipo, en el sentido bueno, redondeados a números legibles (10-100 entero; 1-10 un decimal; < 1 dos; ≥ 100 dos cifras
  significativas). Hacen falta 8.
- `PUT api/Calidad/Tablero/Umbrales` {Kpi, TipoDueno, CortePoorFair, CorteFairGood, CorteGoodFantastic, VigenteDesde?} (Dirección/Informática) →
  UmbralesDTO. Cortes en orden según el sentido (iguales valen). Sin VigenteDesde: el PRIMERO de un KPI y tipo vale desde siempre (03/01/2000: da nota
  al histórico); un cambio, desde el lunes de esta semana; un VigenteDesde pasado con umbrales ya aprobados, 400. El mismo VigenteDesde dos veces
  cambia esa fila. Rastro en Modificaciones (Tabla «CalidadKpiUmbrales», Anterior/Nuevo en JSON) en la misma transacción; Usuario = el del token
  (`UsuarioAuditoriaHelper`).

#### Cómo se aprueban los umbrales
1. Lanzar el script. 2. `POST …/Recalcular?semanas=26` (se programa para la noche). 3. `GET …/Umbrales/Propuesta?kpi=X` y mirar la distribución.
4. Carlos aprueba (o ajusta) con `PUT …/Umbrales`, uno por KPI y tipo de dueño. Hasta entonces, el KPI se enseña sin nota.

### Correo semanal de calidad (27/11/26)
Carlos: «de momento, un correo automático cada lunes, SOLO A MÍ (no a Dirección), con el resumen. Y lo comentaremos en cada sesión de todos modos.»
Código: `Infraestructure/Calidad/ResumenSemanal/` (LA REGLA pura `ReglaCorreoResumenSemanal.Componer(DatosResumenSemanal)` → `CorreoResumenSemanal`
con todos los textos; el pintor puro `PintorCorreoResumenSemanal.Pintar`; `ServicioCorreoResumenSemanal` lee, manda y apunta; job
`CorreoResumenSemanalJobsService`). Controlador `CalidadResumenSemanalController`.

- **Destinatario**: UNA dirección en ParametrosUsuario empresa 1, «(defecto)», `Calidad.CorreoResumenSemanal` (`LeerParametroSinDefecto`: leerla no crea
  fila). Sin fila, vacía, con coma/punto y coma (una lista no vale) o mal escrita → NO se manda nada y ELMAH una sola vez por arranque. Ninguna
  dirección a fuego (el remitente es el de siempre, nesto@). `Scripts/Issue914r_CalidadCorreoResumen.sql` crea la fila VACÍA (idempotente; no la pisa).
- **Job** `calidad-correo-resumen-semanal`: los LUNES a las 7:30 de Madrid, cola «indicadores» (después del tablero de las 4:20). La semana = la de lunes
  a domingo anterior (`ReglaCorreoResumenSemanal.SemanaCerrada`). Si el tablero aún no tiene ESA semana (ningún KPI sin maduración con filas: su job se
  fue a las 21:00 por ir la cola con retraso; la pérdida desconocida siempre guarda los tres almacenes), no manda y se reprograma UNA vez para las 23:00
  (`Reintentar`, que manda con lo que haya).
- **No calcula nada pesado**: el tablero, de `dbo.CalidadKpiSemanas`/`CalidadKpiUmbrales` (las semanas de la tira y la tendencia de todos los KPI, una
  lectura); Almacén, `ServicioCalidadAlmacen.Resumen` de la semana cerrada (con su caché, como el endpoint); encuestas, lo guardado en sombra
  (`ServicioEncuestas.SombraEntre`, la semana; `DiasProcesados` nuevo en el DTO) y el interruptor. Cada parte por su lado: si falla o falta su tabla,
  su sección dice «No disponible: …» (y ELMAH si es un fallo) y el resto del correo sale igual.
- **Idempotente por semana**: el envío se apunta en `dbo.Modificaciones` (Tabla `CalidadCorreoResumenSemanal`, Anterior «Semana yyyy-MM-dd», Nuevo
  {Destinatario, Asunto}, Usuario = el sistema o quien lo pidió); antes de mandar se mira esa fila (Fecha ≥ el lunes siguiente). Ya mandada → no se manda.
  Si no se puede mirar, NO se manda (mejor uno de menos que dos). Se apunta DESPUÉS de que el SMTP lo acepte (si no lo acepta, no se apunta).
- **Contenido** (HTML sobrio, ≤ 640 px, estilos en línea, de usted): asunto «Calidad: semana del 23 al 29/11/2026»; «Lo que más ha cambiado» (hasta 5:
  los KPI/dueños cuya última semana cerrada se separa más, en proporción, de la media de sus 4 anteriores con valor, con `ReglaNotaKpi.Tendencia`
  y `Mejora`; media 0 va delante; «Pérdida desconocida en Alcobendas: 312 € (FAIR), sube respecto a las 4 semanas anteriores (180 €), a peor.»);
  Tablero (por KPI con fuente, la semana si ya está cerrada para él o, si madura, «La semana del … estará el …» y la última cerrada; tabla dueño,
  valor, nota o «sin umbral», y tira de las 8 últimas: P/F/G/★, «·» con valor sin nota, «–» sin dato; sin fuente, «Todavía no disponible»; sin
  filas, «no está calculada»); Almacén (los dos oficiales por preparador y el total, «sin evaluar» o «2,5 % (1 de 40)», con la nota de los N días de
  reclamación, pickings en papel, líneas sin preparador, etiquetas con packing incompleto y el Aviso); Encuestas en sombra (cuántas se habrían
  mandado, de cuántas candidatas y en cuántos días procesados, el tope, por tema y por canal; apagado, lo dice); Pendiente de usted (KPI y tipos sin
  umbrales con la ruta de la propuesta, scripts que faltan del tablero, de las encuestas y los `SinRastro` de Almacén que piden un script, y el
  interruptor `Calidad.EncuestasSombra` apagado); pie con las rutas de la API de cada cifra y que es solo para él.
- **Probarlo**: `POST api/Calidad/ResumenSemanal/Enviar?semana=&ensayo=true` (Dirección/Informática, 403 de usted). Sin `semana`, la última cerrada;
  una semana sin acabar o de hace más de 104 semanas → 400. `ensayo=true` (por defecto) NO manda ni apunta: devuelve {Semana, Ensayo, Enviado, Estado,
  Destinatario, YaEnviadoEl, Asunto, Html, Mensaje} (dice a quién saldría y si ya se mandó). `ensayo=false` lo manda SOLO al configurado (la petición no
  admite otro): 200 Enviado; 409 si ya se mandó esa semana o no hay destinatario; 503 si no se puede comprobar o el SMTP no lo acepta. Es el mismo camino
  que el job (sin el aplazamiento).
