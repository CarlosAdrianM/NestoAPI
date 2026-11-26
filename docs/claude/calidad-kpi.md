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
- Lector: SOLO LECTURA (READ UNCOMMITTED + NOLOCK), una ida por MES (tabla temporal #p con los pedidos del mes y #f con sus facturas; varios
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
| `TieneRectificativaOAbono` | sí | Otro pedido con `PedidoOrigen` = este, o alguna de sus facturas (`Nº Factura` de sus líneas) es `FacturaOriginalNumero` de `LinFacturaVtaRectificacion`. | Siempre |
| `Impagado` | sí | `ExtractoCliente` TipoApunte 4, Importe > 0, Concepto que no empieza por «Gastos Impagado» (como `ModeloImpagados`), mismo empresa + cliente + `Nº Documento` = su factura. | Siempre |
| `IncidenciaDeAgenciaOReenvio` | sí | Envíos del pedido con Estado ≥ 1: más de uno, o alguno en 3 (incidentado), 4 (devuelto) o 5 (cerrado a mano), o con «Reenviar» en `EnviosAgenciaReenvios` (#699), o su caja «ha vuelto» (`EnviosAgenciaHaVuelto`, #870). | Siempre (lo que hay HOY: un incidentado que acabó entregado no deja rastro en GLS) |
| `TocadoTrasElPicking` | sí | «Quitar picking» de Nesto en Modificaciones (Tabla LinPedidoVta, Nuevo «Pedido N…: sin picking (quitado por …») o eventos `QuitarPicking` / `AmpliacionConPickingMismoPedido`. | 09/11/26 (#590) o el primer evento, lo que sea antes |
| `EnvioDeRutaPropiaForzado` | sí | Evento `EnvioRutaPropiaFueraDeDia`. SIN CONECTAR: no hay un punto único donde se fuerce; hoy siempre no evaluado. | Primer evento |
| `EntregaIncompletaDelReparto` | sí | Envío de la agencia 14 con DetalleEstado «Entregado (reparto propio): …» (#880 corte 1) o con filas en `RepartoBultosPendientes` (#880 corte 2). Sin envío de la 14: no aplica (falso, evaluado). | Envíos de la 14 desde el 19/11/26 |
| `PidioCambiarAlgoConPicking` | sí | Eventos `CambioDeModoConPicking` o `AmpliacionConPickingPedidoNuevo`. | Primer evento |

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

- Candidatos vistos y NO conectados (habría que adivinar o no son del pedido perfecto): cobro de más confirmado (`ServicioRepartoPropio` Entregar con
  `ConfirmarCobroDeMas`, `AutorizacionesReembolsoReparto.PideConfirmarDeMas`); cobro repetido o todo a cuenta confirmado (`CobroDeudaReparto.cs`,
  `ReglaCobroRepetidoReparto`); pedido retenido por falta (#858, ya en `PedidosRetenidosPorFalta`); entrega incompleta (#880, ya en
  `RepartoBultosPendientes`); aplazar líneas con picking (`AplazamientoPedidoConPicking`); cambiar el cliente con picking (#876, ya en Modificaciones).
  Pedir autorización de un pedido que no pasa la validación NO cuenta (Carlos 26/11/26).
