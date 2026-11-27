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
