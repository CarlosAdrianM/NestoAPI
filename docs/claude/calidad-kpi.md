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
