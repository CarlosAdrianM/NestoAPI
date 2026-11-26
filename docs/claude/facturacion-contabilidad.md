# Facturación, cobros y contabilidad

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Redondeo, documentos que no salen, Facturae, rectificativas, abonos, proyectos, informes de tienda.
Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.

### Rounding System (Issues #242/#243)
The application uses `RoundingHelper` for all decimal rounding operations:
- **Location**: `Infraestructure/RoundingHelper.cs`
- **Default mode**: `AwayFromZero` (commercial rounding, compliant with Spanish legislation)
- **Rollback**: Set `RoundingHelper.UsarAwayFromZero = false` to revert to VB6-style `ToEven` rounding
- **SQL Server**: Uses `ROUND()` which is already AwayFromZero - no changes needed
- **Auto-fix preventivo**: `ServicioFacturas.CrearFactura()` recalcula las líneas del pedido ANTES de llamar al stored procedure de facturación. Esto previene errores de descuadre causados por diferencias de redondeo entre C# y el SP `prdCrearFacturaVta`. El fix se loguea a ELMAH para diagnóstico.
- **Restricción BD (CK_LinPedidoVta_5)**: `([bruto]=[precio]*[cantidad] OR [tipolinea]<>(1))` - Esta restricción impide modificar Bruto sin modificar también Precio. Por eso el auto-fix NO puede cambiar Bruto.
- **CLAVE PARA EL ASIENTO CONTABLE (02/12/25)**: El SP `prdCrearFacturaVta` construye el asiento usando:
  - HABER Ventas (700): `SUM(ROUND(Bruto, 2))`
  - DEBE Descuentos (665): `SUM(ROUND(Bruto * Dto, 2))`
  - La diferencia (Ventas - Descuentos) debe coincidir con `SUM(BaseImponible)`
- **Cálculo correcto de líneas (coherente con el asiento contable)**:
  - `Bruto = Cantidad * Precio` - **NO se puede redondear** por restricción `CK_LinPedidoVta_5`
  - `ImporteDto = ROUND(Bruto * SumaDescuentos, 2)` - Se redondea el descuento ANTES de restar
  - `BaseImponible = ROUND(Bruto, 2) - ImporteDto` - **USAR ROUND(Bruto, 2)** para que cuadre el asiento
  - `ImporteIVA = BaseImponible * PorcentajeIVA / 100` - Sin redondear (precisión completa)
  - `ImporteRE = BaseImponible * PorcentajeRE` - Sin redondear (precisión completa)
  - `Total = BaseImponible + ImporteIVA + ImporteRE` - Sin redondear (precisión completa)
- **Por qué ROUND(Bruto, 2) en BaseImponible**: Si Bruto tiene más de 2 decimales (ej: 67.4325 de CanalesExternos), la diferencia con ROUND(Bruto,2) (ej: 67.43) se acumula en múltiples líneas y descuadra el asiento. El SP usa ROUND(Bruto,2) para Ventas, así que BaseImponible debe calcularse igual.

Related files:
- `RoundingHelper.cs` - Rounding logic with configurable mode
- `ValidadorDescuentoPP.cs` - Validates early payment discount calculations
- `ServicioFacturas.cs` - Auto-fix preventivo en `CrearFactura()` y `RecalcularLineasPedido()`
- `GestorFacturacionRutas.cs` - Logs detailed diagnostics on mismatch errors
- `GestorPedidosVenta.cs` - `CalcularImportesLinea()` calcula Bruto, ImporteDto, BaseImponible con redondeo correcto
- `RoundingHelperTests.cs` - Tests documentando el comportamiento de redondeo y cálculo de líneas

### Documentos que no salen: empresa espejo, serie GB y pedidos sin IVA (Carlos 25/11/26)
- LA REGLA (pura) `ReglaDocumentoNoDescargable` (`Infraestructure/Facturas/DocumentoNoDescargable.cs`): no sale hacia fuera (a) nada de la empresa
  espejo (3), (b) una factura de una serie sin descarga (`CorreoDesdeFactura` null: GB, lo de siempre) ni (c) el pedido, albarán, proforma,
  presupuesto o nota de un pedido que va a la espejo (`ServicioTraspasoEmpresa.VaAEmpresaEspejo(cab.IVA)`: IVA NULL o en blanco). 403 con un
  único texto «El documento {número} no se puede descargar.». `ComprobadorDocumentoDescargable` lee la cabecera (pedido o albarán que no
  existe: sigue y da su «no existe»).
- Salidas: GET api/Facturas (PDF; número entero = pedido) y api/Facturas/FacturaJson, GET api/AlbaranesVenta, POST api/Facturas/EnviarPorCorreo
  (empresa 3), enlace de descarga/WhatsApp (`ProveedorDocumentosEnlace`: ni se crea ni se sirve; un enlace antiguo da la página de «no
  encontrado»), correo del pedido (`ProveedorDocumentosCorreo.PdfPedido`), envío diario y trimestral (fuera la empresa 3) y adjuntos de las
  domiciliaciones (ni espejo ni GB). El correo de agencias ya lo hacía desde 2020 (`GestorEnviosAgencia`, IVA null o pedido en la 3).
- NO pasa por aquí (interno, a propósito: la regla no está dentro de `GestorFacturas.LeerPedido/LeerAlbaran`): Facturar rutas y su impresión,
  documento de la nota de entrega/recogida, packing (`PasosDocumentosEnPapelApi`, que llama al gestor y no al endpoint), etiquetas, correo
  interno del pedido, prueba de entrega.
- **Las cuatro que quedaban (Carlos 25/11/26)**: (1) «No se puede imprimir en ningún caso»: `GET api/PedidosVenta/{empresa}/{pedido}/DocumentosImpresion`
  (Cajas de Nesto tras cobrar) con empresa espejo, pedido sin IVA o factura que no se sirve → 200 con las tres listas vacías y `Mensaje` = el único
  (LA REGLA `ReglaDocumentoNoDescargable.DocumentoQueNoSeImprime`, que elige el documento como el gestor: factura salvo FDM, albarán o nota): el cobro
  sigue, solo no se imprime (Nesto, sin cambios: sin bytes nuevos se queda con los de CrearFactura). Y `CrearFactura` en papel (`DocumentoFacturaDetalle`):
  una factura de la espejo o de serie sin descarga no lleva PDF ni se imprime en el servidor (Nesto no imprime sin `DatosImpresion`). `DebeImprimirDocumento`
  solo mira comentarios: sin cambios. (2) `GET api/Facturas/Facturae` (también `comprobar=true`): 403 texto con el mensaje único, antes de leer nada
  (después del 403 de permisos). (3) 347 (`ExtractosClienteController.GetModelo347` y `Modelo347Pdf`): con la empresa espejo, 403 «El documento Modelo 347
  no se puede descargar.»; con la 1 nunca mezcló empresas (filtro `Empresa == empresa`, ahora en `MovimientosModelo347`, mismas cifras). (4)
  `ServicioFacturasAmazon.FacturarYSubirAsync`: pedido sin IVA o de la espejo (antes de preguntar a Amazon ni facturar) o factura resultante que no se
  sirve (antes del PDF) → no sube, ELMAH «[Amazon] factura no descargable omitida: …» y respuesta Estado OMITIDA con el mensaje en Avisos.

### Facturae para FACe por B2Brouter (NestoAPI#903, 24/11/26; gemela Nesto)
- Cinco o seis facturas al año a organismos públicos: la API genera el fichero Facturae 3.2.2 SIN firmar de una factura YA emitida y Carlos lo
  importa en b2brouter.net («importar factura emitida»), que lo firma y lo manda a FACe. Nada de certificados, firma ni envío. Solo LECTURA.
- `GET api/Facturas/Facturae?empresa=1&factura=NV26…` (`FacturaeController`; Administración, Dirección, Informática; cliente de la tienda 403) →
  `application/xml` UTF-8 «Facturae_{número}.xml»; 404 sin factura; 400 de usted con TODO lo que falta. Opcionales que GANAN sobre lo guardado:
  `oficinaContable`, `organoGestor`, `unidadTramitadora`, `organoProponente`, `suPedido`, `expediente`, `contrato`. `comprobar=true` → JSON
  `ComprobacionFacturaeDTO` (Listo, Faltas, Avisos, centros, referencias, base, impuestos, total, vencimientos con PaymentMeans e IBAN).
- Datos (`ServicioFacturae`, sin lecturas nuevas de facturación): la factura del PDF (`GestorFacturas.LeerFactura`: datos fiscales guardados, NIF,
  totales, vencimientos e IBAN de la empresa), sus líneas (`CargarCabFactura`: IVA y RE por línea), el NIF de la empresa (`CargarEmpresa`), las
  originales de una rectificativa (`ServicioFacturas.CargarFacturasRectificadas`, la de Verifactu, ahora internal) y los DIR3. Emisor = dirección
  «Empresa» del PDF; receptor = la «Fiscal»; los centros llevan la dirección fiscal del cliente. ReceiverTransactionReference de cada línea (Carlos
  25/11/26, «si tenemos PO lo ponemos; si no, ponemos el nuestro»): el parámetro `suPedido` > `CabFacturaVta.SuPedido` (#723) > el número de
  NUESTRO pedido de esa línea (`LinPedidoVta.Número`; una factura de varios pedidos lleva el suyo en cada línea) con el aviso «No consta el pedido
  del organismo: se pone nuestro número de pedido (931472) como referencia.» (ya no el de «falta la referencia»). `comprobar=true`: `SuPedido` = lo
  que sale (varios, separados por coma) y `OrigenSuPedido` «Parametro» | «Factura» | «NuestroPedido». Expediente (FileReference) y contrato solo
  por parámetro (TextMax20: más largo = falta; un P.O. de más de 20 es falta, no se cambia por el nuestro).
- LA REGLA (pura) `ConstructorModeloFacturae` + escritor `GeneradorFacturae` (`Infraestructure/Facturas/Facturae/`): cabecera por (IVA, RE) como el
  PDF y Verifactu (base = suma; cuota = ROUND(suma ImporteIVA, 2); RE = ROUND(suma RE × base, 2)); línea: TotalCost = Bruto, Discount = ImporteDto
  (con DiscountRate), el redondeo ROUND(Bruto,2) − Bruto como Charge (sube) o Discount (baja) «Redondeo a céntimos», GrossAmount = la base; cuotas de
  línea a 2 y la línea mayor de cada tipo se lleva el céntimo. Si el total no es el de la factura o los vencimientos no lo suman: falta. Número SIN serie
  (`NumeroSinSerie`) + InvoiceSeriesCode; BatchIdentifier = NIF + número + serie. Rectificativa: OR + Corrective (ReasonCode 16, CorrectionMethod 02
  «por diferencias», la «I» de Verifactu; varias originales: la primera y las demás en el texto adicional; sin vinculación: falta). Pago: TRN y CNF →
  04 con el PRIMER IBAN de la empresa (aviso si hay varios), RCB 02, EFC 01, TAR 19; otra (CHC…) → falta. Hijos sin espacio de nombres (unqualified).
- DIR3: `dbo.ClientesFacturaElectronica` (Empresa, Cliente, Contacto; OficinaContable, OrganoGestor, UnidadTramitadora, OrganoProponente NULL y
  sus nombres; fuera del EDMX, `Scripts/Issue903_ClientesFacturaElectronica.sql`). `GET/PUT api/Clientes/FacturaElectronica` (mismos grupos; PUT: los
  tres obligatorios, `ReglasDir3`: 9, letra + al menos 5 cifras; los tres vacíos = quitar; rastro en Modificaciones; sin tabla 503, el GET vacío).
  Lectura: la del contacto o, si el cliente tiene UNA sola fila, esa. Siguiente corte (si hace falta): subirlo por la API de B2Brouter.

### Rectificativa de una rectificativa (NestoAPI#892, caso RV2600189, Carlos 24/11/26: se permite y se declara bien)
- Invertir una RV con destino en serie rectificativa (añadir a su propio pedido RV) = `ReglaRectificativaDeRectificativa.EsRectificarRectificativa`
  (`Infraestructure/Rectificativas/RectificativaDeRectificativa.cs`): se vinculan las líneas POSITIVAS a la línea de la RV de origen (`SeVincula`), al facturar
  en el acto (`GuardarVinculacionesRectificativa`) o en `RectificativaPendiente`; se envía como R por diferencias de esa RV (el mapeador no mira el signo).
  Con pedido nuevo sigue siendo deshacer el abono en NV (`EsDeshacerAbono`, 11/11/26). Lo de siempre (vincular las negativas) no cambia.
  Un abono no se deshace dos veces (Carlos 24/11/26): `MotivoNoSePuedeAbonar` + `ReglasAbonoCopiaFactura.MotivoYaCorregida` (todo o nada, como la de ventas)
  paran invertir una RV con alguna línea negativa ya vinculada o pendiente («La rectificativa RV2600186 ya está corregida por la RV2600189…»), vaya a RV o a NV;
  lo deshecho por el camino NV deja desde NestoAPI#907 (25/11/26) su rastro en Modificaciones (Tabla `AbonoDeshecho`, Anterior «empresa|RV», Nuevo = JSON
  con el pedido nuevo y las líneas negativas deshechas; `AbonosDeshechos.cs`, sin script) y `MotivoNoSePuedeAbonar` lo suma a las correcciones: solo
  las líneas que siguen en el pedido nuevo (borrado = ya no bloquea), con su factura o «el pedido N (sin facturar)». Si el rastro no se puede leer o
  escribir, ELMAH y como antes. Lo deshecho por NV antes del 25/11/26 no tiene rastro.
- Auto-curado del job (`ServicioFacturas.GenerarVinculacionesRectificativaDeRectificativa`, tras el LIFO de #38 si este no hace nada): RV/RC sin
  vinculaciones, sin ninguna línea negativa, todas las positivas con producto. LA REGLA (pura) `Casar`: todas casan una a una (producto, cantidad exactamente
  contraria) con UNA rectificativa anterior del mismo cliente (no una NV con negativa), sin lo ya corregido (vinculaciones o pendientes); gana la del mismo
  pedido, si no la más reciente; dos del mismo día sin la del mismo pedido → no cura. Si no cura, el correo del job dice «rectificativa en positivo» (no «devolución»).

### Copiar o abonar solo unos albaranes o unas líneas de una factura (NestoAPI#899 y #863, Carlos 24/11/26)
- Por qué: para dejar una factura por P.O. (27593, NV2616046) hubo que copiar facturas ENTERAS y borrar líneas con un script, porque la API no
  dejaba elegir y el PUT del pedido copiado recalculaba los portes; y una devolución PARCIAL de la agencia (#863) obligaba a abonar y rehacer todo.
- `POST api/PedidosVenta/CopiarFactura` admite, opcionales y aditivos: `Albaranes` [Nº Albarán] y `Lineas` [{NumeroOrden (alias de entrada
  `NumeroLinea`), Pedido? (solo si la factura agrupa pedidos y ese Nº Orden está en varios), Cantidad? (1..|la de la línea|; sin ella, entera)}],
  y `LineasNoVendibles` [Nº Orden] (#863 corte 1: solo un comentario «[Vuelve estropeado, no vendible: … Hay que regularizar el stock a mano]» en el
  abono). Sin ellos, todo como siempre. LA REGLA (pura) `ReglaSeleccionCopiaFactura.Elegir` (`Infraestructure/Rectificativas/SeleccionCopiaFactura.cs`):
  entran las de los albaranes (SIN las de cuenta contable: portes y comisión solo si se piden como línea) más las líneas pedidas (manda su cantidad,
  con el signo de la línea); todos los errores juntos y de usted (albarán o línea que no es de la factura, repetida, cantidad 0 o de más, Nº Orden
  ambiguo, nada que copiar) → 400 y nada creado. Solo con UNA factura (`MotivoVariasFacturas`, 400 `COPIA_FACTURA_SELECCION_VARIAS_FACTURAS`).
- Copia FIEL: con selección se copian precio, descuentos y DescuentoPP de la factura aunque haya cambio de cliente y `MantenerCondicionesOriginales`
  = false (decisión: «no recalcula portes ni precios»); la copia nunca pasó por el PUT, así que no se añaden portes. La línea origen no se toca
  (las cantidades elegidas van aparte, en memoria). Comentario «[Copia parcial de factura X: albaranes 101, 104; líneas 1 (2 unidades de 5)]»
  o «[Rectificativa parcial de …]»; en el rehecho, «… [Solo: …]».
- No abonar dos veces, por unidades: `ReglasAbonoCopiaFactura.LineaVendida.CantidadQueSeAbona` (null = entera, como antes): `MotivoYaAbonado` deja si
  ya abonado (rectificativas + RectificativaPendiente) + lo de ahora ≤ lo vendido («17404 (4 de 5 ya abonadas: quedan 1 y se piden 2)»);
  `MotivoYaCorregida` igual para deshacer una parte de una línea negativa. Con la línea entera, lo de siempre (cualquier abono anterior la bloquea).
  Vinculaciones (`LinFacturaVtaRectificacion`, al facturar o en RectificativaPendiente) y rastro de #907 (`ReglaAbonoDeshecho.Construir`, ahora con la
  cantidad COPIADA): solo de lo copiado.
- Los tres modos: abono, abono + rehacer (#699: abono y pedido nuevo con lo mismo; sin `IncluirPortes` el rehecho lleva además los gastos de envío de
  la factura a 0 € aunque no se elijan, `GestorCopiaPedidos.LineasDelPedidoRehecho`, para que el picking no los cobre otra vez) y abono + cargo (los
  dos con la misma selección). Respuesta: `UnidadesCopiadas` y `ImporteCopiado` (aditivos, de `LineasCopiadas`).
- `GET api/PedidosVenta/CopiarFactura/Lineas?empresa=&factura=` (`CopiarFacturaLineasController`, cualquier empleado; cliente de la tienda 403; 400 sin
  datos; 404 sin factura; vale el número de pedido de una sola factura) → {Empresa, Factura, Albaranes, Lineas [{Pedido, NumeroOrden, Albaran,
  FechaAlbaran, TipoLinea, EsCuentaContable, EsGastosEnvio, Producto, Texto, Cantidad, YaAbonada, QuedanPorAbonar, Precio, BaseImponible}]}. Sin
  códigos de barras todavía (los pedía #863 para leer con la pistola).
- Gemela en Nesto (#638): en «Copiar factura», marcar albaranes o líneas y cantidad, con lo ya abonado (el GET) y la casilla «no vendible».

### Abonar solo lo comprado (NestoAPI#891, Carlos 24/11/26; EN SOMBRA, no frena nada)
- LA REGLA (pura, lo decidido) `ReglaAbonoSoloLoComprado` (`Infraestructure/PedidosVenta/AbonoSoloLoComprado.cs`): una línea de abono (producto, cantidad < 0,
  -1..2; sin cupón ni cheque regalo, que son descuentos) cabe si facturadas − abonadas en factura − abonadas en OTROS pedidos sin facturar ≥ lo que abona, sin
  límite de tiempo; texto «Del 41752 este cliente tiene facturadas 3 y ya abonadas 3: no se puede abonar 1 unidad.». Lectura en UNA consulta
  (`LectorCompradoParaAbonoSql`, LinPedidoVta Estado 4, NOLOCK). Copiar factura ya frena por línea (`ReglasAbonoCopiaFactura`, #892/#907): no se toca.
- Sombra: tras guardar (POST y PUT de PedidosVenta), si alguna no cabe, una línea en ELMAH «[Abono de lo no comprado #891, sombra]» (cliente entero,
  empresas 1 y 3: solo lo que no cabría de ninguna manera). Sin líneas de abono no lee nada; nunca lanza. POR DECIDIR (issue): dónde frena, cliente o
  contacto, empresas, otros caminos (recogidas #757, devueltos, Nesto viejo), descuentos en negativo y quién se la salta.

### Proyectos: imputar ingresos y gastos y cuenta de explotación (NestoAPI#901 corte 1, Carlos 25/11/26)
- Cada convocatoria de un curso del SEPE (IMPE0210-2026-1), feria, evento o campaña es un PROYECTO. Se imputan DESPUÉS de contabilizar apuntes de
  Contabilidad (cuentas 6 y 7) por IMPORTE (se enseña el porcentaje); la contabilidad ni sus SP se tocan. `dbo.Proyectos` + `dbo.ProyectosImputaciones`
  (fuera del EDMX, `Scripts/Issue901_Proyectos.sql`; sin ellas, 503 «SIN_TABLA»). Cada imputación guarda una FOTO del apunte (cuenta, fecha, concepto,
  documento, asiento, debe, haber, tercero del asiento por ExtractoProveedor/ExtractoCliente).
- CONVENCIÓN DE SIGNOS (una sola, `ReglasProyectos`): importe del apunte = |Debe − Haber|; la imputación SIEMPRE positiva; 6xx = Gasto, 7xx = Ingreso; en
  su lado natural (gasto al Debe, ingreso al Haber) suma y al contrario (abono) RESTA (`Signo`, `Efecto`). Resultado = ingresos − gastos; margen sobre ingresos.
- LAS REGLAS (puras): `ReglasProyectos` (código ≤ 20, A-Z/cifras/- _ . /, mayúsculas, único por empresa → 409 CODIGO_REPETIDO; tipos CursoSEPE/Evento/Feria/
  Campana/Otro; cerrado = sin cambios ni imputaciones nuevas, PROYECTO_CERRADO; borrar solo abierto y sin imputaciones; `DecidirReparto`: el PUT SUSTITUYE
  el reparto de un apunte, todo o nada, misma empresa, importes > 0, un proyecto una vez, suma ≤ apunte + 1 céntimo, 409 APUNTE_NO_EXISTE/APUNTE_CAMBIADO,
  aviso si el apunte cae fuera de las fechas del proyecto) y `CalculoCuentaExplotacionProyecto` (cuenta de explotación, totales y comparativa).
- `RepositorioProyectos` (ADO, NestoConnection): el reparto va en una transacción con `sp_getapplock` «ProyectoImputacionApunte:{empresa}:{nº orden}» (otro
  a la vez → 409 APUNTE_OCUPADO) y los proyectos leídos con UPDLOCK (cerrar espera); rastro en Modificaciones (Tablas `Proyecto` y `ProyectoImputacion`, JSON
  antes/después). Apuntes por imputar: UNA ida (página topada a 500 + sus imputaciones), NOLOCK.
- `ProyectosController` (Administración, Dirección, Informática; cliente de la tienda 403; Reabrir solo Dirección): `GET/POST api/Proyectos`, `GET/PUT/DELETE
  api/Proyectos/{id}`, `POST …/{id}/Cerrar|Reabrir`, `GET api/Proyectos/ApuntesPorImputar`, `GET/PUT api/Proyectos/Apuntes/{nº orden}/Imputaciones`,
  `GET …/{id}/CuentaExplotacion[/Excel]` (hojas «Resumen» y «Detalle»), `GET api/Proyectos/Comparativa?ids=|producto=`. Errores {Message, Codigo}.
- Segundo corte (sin hacer): cuentas obligatorias con control «sin imputar» y aviso a Administración, reglas que proponen (proveedor + cuenta → proyecto),
  horas de personal propio (coste interno sin apunte), propuesta de las facturas de las inscripciones del evento y la pantalla gemela de Nesto.

### Auditoría de seguridad: efectos del extracto (Nesto#644, hallazgos 8 y 2, 17/11/26)
- `PUT api/ExtractosCliente` (solo lo llama Nesto, ficha de cliente, con el token de Windows): `[Authorize]` + Administración/Dirección/Informática
  (`ExtractosClienteController.PuedeModificarEfecto`; 403 de usted). Escribe solo lo que cambia (textos sin relleno ni mayúsculas, null = vacío;
  vencimiento sin FechaVto = Fecha); sin cambios, 400 (antes 500). `IniciarPago` (`POST api/Pagos` y los cobros internos) rechaza con 400 un efecto que
  no existe o es de otro cliente (`ServicioPagos.MotivoEfectosDeOtroCliente`; no mira empresa ni contacto del cobro: 1/3 y varios contactos valen) y
  pone a cada efecto el contacto de su apunte.

### Facturado en tienda: acumulado del año y comparativo (NestoAPI#719, gemela de Nesto#559, 12/11/26)
- `GET api/Informes/FacturacionTienda` (y `/Excel`) con `tipo=Semana|Acumulado|Comparativo` (vacío = Semana, lo de siempre),
  `hasta` (sin él, hoy), `anios` (Comparativo: 2 por defecto, 1-10) y `almacen` (alias de `tienda`); mismos permisos y tienda que la semana.
- Acumulado/Comparativo → `FacturacionTiendaAnualDTO` (meses de enero al de `hasta` + `Total`; cada mes con `Anteriores` y variación € y %
  del año de `hasta` respecto a cada año). Una consulta agregada por año y mes (`SQL_FACTURACION_TIENDA_ANUAL`), del 1 de enero al mismo
  día y mes de cada año. Excel: hoja «Acumulado» y, en el comparativo, también «Comparativo». El correo (POST …/Correo) sigue siendo de la semana.

### Alquileres: equipo y nombre del cliente en la relación (Novedades 1181, 12/11/26)
- `GET api/Alquileres/Cabeceras` y `…/Cabeceras/PorCliente` traen además (solo lectura, `AlquilerCabeceraDTO`) `ProductoNombre` (ficha de
  Productos; si no, la copia `CabAlquileres.NombreProducto`) y `ClienteNombre` (contacto del alquiler o, si falta o no casa, el principal), con
  una consulta por tabla (sin N+1). `NombreCliente` sigue siendo solo el del contacto exacto (etiqueta). El guardado los ignora y Nesto no los manda.

### Una factura por P.O.: nunca se juntan pedidos de P.O. distintas (NestoAPI#898, caso 27593 / NV2616046, Carlos 26/11/26)
- La P.O. es `CabPedidoVta.SuPedido` (#195/#723; `PedidoVentaDTO.suPedido`), no el comentario. LA REGLA (pura) `ReglaPOAgrupacion`
  (`Infraestructure/Facturas/Agrupacion/ReglaPOAgrupacion.cs`): dos pedidos solo van juntos con la MISMA P.O. (`Clave`: sin relleno ni
  mayúsculas; NULL o en blanco = «sin P.O.», que solo va con otros sin P.O.). `MotivoNoSePuedenJuntar` / `MotivoNoSePuedenUnir` dan la frase.
- Dónde se junta en la API y cómo la aplica: (1) agrupación por P.O. (`EstrategiaAgrupacionPO`: Facturar rutas y `POST api/FacturacionRutas/AgruparPorPO`)
  agrupa por empresa + cliente + `Clave` + IVA (antes por la P.O. recortada CON mayúsculas: «po1» y «PO1» daban dos facturas; `GrupoPedidosPO.SuPedido`
  = la del pedido de menor número, tal cual); (2) fin de mes (`EstrategiaAgrupacionFinDeMes`, `POST api/FacturacionRutas/FinDeMes`): solo pedidos SIN
  P.O. (ya era así desde #727; la clave lleva la P.O. por explícita); (3) `MotorAgrupacionPedidos.Agrupar`, ÚLTIMA BARRERA: con P.O. distintas lanza
  `InvalidOperationException` con el motivo ANTES de mover nada (los dos servicios lo recogen por grupo y siguen); (4) «Unir pedidos»
  (`GestorPedidosVenta.ComprobarPOAlUnir`, antes de mover líneas): 400 `NestoBusinessException` código `PEDIDOS_PO_DISTINTAS` «No se pueden unir: el
  pedido 926287 es de la P.O. PO00474329 y el 927198 es de la P.O. PO00481139. Cada P.O. va en su propia factura: …»; con P.O. y sin P.O. tampoco. Una
  ampliación NUEVA de la plantilla (número 0) sin P.O. se une como siempre (toma la del original); con otra P.O., 400.
- La factura conserva la P.O. del destino (prdCrearFacturaVta copia `SuPedido` a `CabFacturaVta.SuPedido`): sin cambios. El motor no toca el
  `Contacto` de las líneas (cada tienda sigue en su línea). Facturar rutas factura pedido a pedido: no junta.
- FUERA de la API (no se ha tocado): el fin de mes de Nesto viejo = `prdAgruparAlbaranesVta` (por cliente+contacto; desde
  `Scripts/Issue727_prdAgruparAlbaranesVta_PO.sql` no mezcla P.O. ni con/sin P.O.) + la reunión de TODOS los contactos en un pedido nuevo que hace
  el VB6 SIN mirar la P.O. y poniendo contacto 0 a las líneas: así salió la NV2616046 (927473). Con `Facturacion:FinDeMesPorApi` encendido el fin de
  mes debe hacerse por la API. La P.O. escrita solo en el comentario («P.O. 00474329») no la ve nadie: hay que ponerla en «Su pedido».

## Fin de mes de Nesto viejo capado y P.O. entera o nada (NestoAPI#898, Carlos 26/11/26, urgente)
- `Scripts/Issue898_FinDeMesNestoViejo_y_POEntera.sql` (sa; lee la definición del servidor e inserta un bloque en cada procedimiento, con marca
  «NestoAPI#898-…»; si la cabecera o el punto de inserción no cuadran, no cambia nada): (1) `prdAgruparAlbaranesVta` llamado desde Nesto viejo
  (`fnEsNestoViejo`) → error «…hágalo en Nesto (Facturar rutas, «Fin de mes»)…» y no agrupa nada (mecanismo de #590: ParametrosUsuario
  `NestoViejo_FinDeMes_Modo` «(defecto)» = Bloqueo | Sombra y `NestoViejo_FinDeMes` = 1 por usuario; rastro en `NestoViejoSombra`). También corta el
  «agrupar albaranes» de un cliente en Nesto viejo. El fin de mes va por la API (`POST api/FacturacionRutas/FinDeMes`, en uso desde el 30/10/26).
  (2) `prdCrearFacturaVta` (por él factura todo): un pedido con «Su pedido» (P.O.) NO se factura si le queda alguna línea -1..1 con cantidad, ni si
  otro pedido del mismo cliente y la misma P.O. (recortada de los extremos, nada más) tiene líneas -1..2 (hay que agrupar por P.O. antes): «una P.O. va
  en una sola factura». Carlos: «o se factura todo y se recoge lo no entregado, o no se factura nada hasta que esté todo albaraneado». POR PEDIDO desde
  `Scripts/Issue898b_POEntera_PorPedido.sql` (26/11/26): cualquier pedido con «Su pedido», no solo los clientes de `ClientesSuPedidoObligatorio`;
  también el texto libre («ENSEÑAR A SANTY», 3 de 37 en 90 días): ese pedido tampoco se factura a medias (si hace falta, se vacía el campo).
  Interruptor «(defecto)» `Facturacion.POSoloEntera` (sin fila o ≠ «0» = encendido).
- La MISMA regla en la API, antes del procedimiento y con mensaje propio: LA REGLA (pura) `ReglaPOEntera` (`Infraestructure/Facturas/POEntera.cs`;
  la P.O. con `ReglaPOAgrupacion`), interruptor `InterruptorPOSoloEntera` (la misma fila, sin crear filas, caché 1 min; solo «0» apaga, como el
  procedimiento) y la lectura `LectorPOEnteraSql` (UNA consulta por tanda y empresa, solo los pedidos con P.O.). `ServicioFacturas.CrearFactura`
  (`ComprobarPOEntera`, tras el «FDM sin agrupar» y ANTES de traspasar a la espejo) → `FacturacionException` `FACTURACION_PO_NO_ENTERA` (400):
  «El pedido N es de la P.O. X y no está todo entregado: una P.O. va en una sola factura. Entregue lo que falta, o quítelo del pedido, y vuelva a
  facturar.» / «La P.O. X tiene más pedidos sin facturar (el N, por ejemplo): agrúpelos antes por P.O. cuando estén todos entregados.». Si la lectura
  falla, sigue (ELMAH): el procedimiento es la última barrera. Facturar rutas (`GestorFacturacionRutas`): la (b) se lee al empezar para toda la tanda
  (`LeerPOEnteraTanda`) y la (a) en memoria con las líneas recargadas DESPUÉS del albarán (`MotivoPOEntera`); el albarán se hace, la factura se salta
  con el motivo en `PedidosConErrores` (TipoError «Factura», Warning, como el «al completar») y sale el papel del albarán si toca; los demás siguen.
  Fin de mes por la API: no coge pedidos con P.O. (van por «Agrupar por P.O.»); si alguno llegara, CrearFactura lo rechaza y va a `Errores`.
