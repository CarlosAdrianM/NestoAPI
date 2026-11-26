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

### Remesas: probabilidad de devolución de cada recibo, EN SOMBRA (NestoAPI#896, 26/11/26)
- Decisión de Carlos (24/11/26): se hace YA el modelo, el entrenamiento programado y la sombra (predicción guardada y su resultado) y NO se enseña
  nada en Nesto ni en NestoApp (ni porcentaje ni marcas) hasta que la sombra diga que es fiable. `GET api/Remesas/EfectosCandidatos` NO cambia.
  Interruptor de ENSEÑAR: ParametrosUsuario «(defecto)» `Remesas.ModeloImpagadosEnsenar` (solo «1»; nace a «0»; leerlo no crea fila;
  `InterruptorEnsenarImpagos`): hoy solo sale en la comparación, ese corte (candidatos, extracto, ficha) está SIN hacer.
- Todo en `Infraestructure/Remesas/ModeloImpagados/` (patrón de ModeloPlazos #853). Fuente: `ExtractoCliente`. Recibo girado = cartera (TipoApunte 2)
  con `Remesa` (para entrenar, FormaPago RCB); impagado = TipoApunte 4 cuyo Concepto es el motivo (el de «Gastos Impagado…» no cuenta), enlazado por
  empresa + cliente + Nº Documento + Efecto al ÚLTIMO giro anterior sin impagado (`EnlazadorImpagados`, puro). OJO: la `Fecha` de la cartera es la
  de la FACTURA; el día del GIRO es el de la remesa (`Remesas.Fecha`; sin fila, el vencimiento; `ReglaFechaGiro`). Etiqueta: devuelto en los 45 días
  siguientes al giro (`ConstructorRasgosImpagos.DIAS_ETIQUETA`); los giros de los últimos 45 días no entrenan.
- Rasgos SIN FUGA (`ConstructorRasgosImpagos.Rasgos`, puro: solo recibos del cliente girados ANTES de ese día y devoluciones con impagado ANTERIOR a ese
  día): días desde la última devolución y su motivo (Saldo/Mandato/Cuenta/Disconforme/Otro), devoluciones en 3 y 12 meses y totales, recibos
  anteriores y en 12 meses, tasa del cliente, antigüedad, recibos girados en los últimos 45 días (aún pueden volver), importe y su relación con el
  habitual (mediana), regiro (ese documento y efecto ya volvió), primer giro a esa ficha bancaria, nº de efecto, días de la factura al giro, día del
  mes, mes, día de la semana, nivel general de 14 y 30 días (`NivelGeneralImpagos`: impagados de todos / recibos girados 8 días antes o más), forma de
  venta, vendedor y entidad de la ficha CCC (las poco frecuentes, «(otros)»; `MapaCategoriasImpagos`). Fuera a propósito: deuda vencida e impagados
  pendientes (ImportePdte es el de HOY) y antigüedad del mandato (CCC.FechaMandato se cambia tras una devolución: sería fuga).
- LA REGLA simple (`ReglaImpagos`, pura; la del análisis, prueba_modelo.py): 7 rasgos por tramos (última devolución, devoluciones 12 meses, recibos
  anteriores, importe, nivel general, día del mes, recibos en 45 días); peso = log-odds de la tasa suavizada (+20) menos el de la base; probabilidad =
  sigmoide(base + 0,6 × suma). Los pesos se AJUSTAN en cada entrenamiento (no van en el código) y se guardan SIEMPRE (`impagos_remesas_regla.json`):
  es el respaldo y la vara de medir.
- Modelo: LightGBM binario (ML.NET 4.0.2) + calibración de Platt propia (`CalibradorPlatt`, ajustada con recibos que el árbol no ha visto).
  `EntrenadorModeloImpagos`: validación = últimos 6 meses de giros resueltos, calibración = los 6 anteriores. Métricas (`CalculoMetricasImpagos`): AUC,
  % de devoluciones recogidas en el 5 % y el 10 % más arriesgado, tramos de Carlos (< 5 %, 5-15 %, 15-30 %, ≥ 30 %) previsto/real y calibración de los
  altos (≥ 15 %: reales / previstos). `PuertaCalidadImpagos`: no promueve si ordena peor que LA REGLA (AUC −0,01) ni si empeora al guardado (AUC −0,01,
  captado 10 % −3 puntos); ≥ 30 devoluciones en validación. Modelo en `ModeloLlamadas:Carpeta` (`impagos_remesas.zip` + `.json`).
- Job `remesas-impagos-modelo` (DOMINGOS 4:40 Madrid, cola «entrenamiento»; lee 8 años con NOLOCK, el primero solo da historia): apunta SIEMPRE en
  `dbo.RemesasModeloEntrenamientos` (métricas de modelo y regla, tramos en JSON, Platt, ModeloActivo, Motivo). `POST api/Remesas/ModeloImpagados/Entrenar
  [?ahora=true]` (Dirección/Informática; 202; L-V 8-20 → a las 21:00).
- Sombra: punto ÚNICO `RemesasService.CrearRemesaAsync` → `SombraImpagosRemesa.AlCrearRemesa` (solo ENCOLA en Hangfire, cola «indicadores»; nunca lanza,
  ELMAH una vez): el trabajo lee los efectos ya girados (cartera con ese nº de remesa), la historia de SUS clientes (2 consultas por tanda de 500) y el
  nivel general (2 agregadas entrando por las remesas recientes) y apunta en `dbo.RemesasPrediccionesImpago` una fila por efecto: VersionModelo,
  Probabilidad (NULL sin modelo guardado), ProbabilidadRegla, Rasgos (JSON) y MotivosRiesgo (2 causas: contribución por rasgo del árbol,
  `ExplicacionModeloContacto.ContribucionesDe`; si no, los pesos de la regla; frases de `TextosRiesgoImpago`). Idempotente (empresa + remesa + Nº orden).
  Sin regla ni modelo guardados (antes del primer entrenamiento) no apunta nada. Las remesas hechas fuera de la API (Nesto viejo) no pasan por aquí.
- Job `remesas-impagos-resultado` (todos los días 3:55, cola «indicadores», lotes de 500 Id, idempotente): Devuelto = 1 + fecha y motivo en cuanto hay
  impagado del mismo cliente, documento y efecto en los 45 días siguientes; Devuelto = 0 al cumplirlos.
- `GET api/Remesas/ModeloImpagados/Comparacion?semanas=12` (Dirección/Informática; 1-104; 503 sin script): UltimoEntrenamiento, Sombra (Predicciones,
  Pendientes, Modelo y Regla por tramos previsto/real), Fiable, FiableDesde, SemanasSeguidasCumpliendo, Veredicto y SemanasEvaluadas. «Fiable»
  (`ReglaFiabilidadImpagos`, pura): cada semana CERRADA (su domingo + 45 días ya pasó) se mide con la ventana de las 12 semanas que acaban en ella
  (una semana sola trae ~15 devoluciones): AUC ≥ 0,80, calibración de los tramos altos dentro de ±25 %, ≥ 30 devoluciones y ≥ 30 recibos altos; 4
  semanas seguidas → fiable.
- `Scripts/Issue896_RemesasModeloImpagos.sql` (sa; las dos tablas, índices, GRANT y el interruptor a «0»). Sin él: remesas igual, jobs sin hacer nada
  (ELMAH una vez), endpoints 503. Tras lanzarlo hay que entrenar una vez (domingo o POST Entrenar) para que la sombra empiece a apuntar.

## Fin de mes de Nesto viejo capado y P.O. entera o nada (NestoAPI#898, Carlos 26/11/26, urgente)
- `Scripts/Issue898_FinDeMesNestoViejo_y_POEntera.sql` (sa; lee la definición del servidor e inserta un bloque en cada procedimiento, con marca
  «NestoAPI#898-…»; si la cabecera o el punto de inserción no cuadran, no cambia nada): (1) `prdAgruparAlbaranesVta` llamado desde Nesto viejo
  (`fnEsNestoViejo`) → error «…hágalo en Nesto (Facturar rutas, «Fin de mes»)…» y no agrupa nada (mecanismo de #590: ParametrosUsuario
  `NestoViejo_FinDeMes_Modo` «(defecto)» = Bloqueo | Sombra y `NestoViejo_FinDeMes` = 1 por usuario; rastro en `NestoViejoSombra`). También corta el
  «agrupar albaranes» de un cliente en Nesto viejo. El fin de mes va por la API (`POST api/FacturacionRutas/FinDeMes`, en uso desde el 30/10/26).
  (2) `prdCrearFacturaVta` (por él factura todo): el pedido con P.O. de un cliente de `ClientesSuPedidoObligatorio` (hoy solo el 27593) NO se
  factura si le queda alguna línea -1..1 con cantidad, ni si otro pedido del mismo cliente y la misma P.O. tiene líneas -1..2 (hay que agrupar por
  P.O. antes): «una P.O. va en una sola factura». Carlos: «o se factura todo y se recoge lo no entregado, o no se factura nada hasta que esté todo
  albaraneado». Interruptor «(defecto)» `Facturacion.POSoloEntera` (sin fila o ≠ 0 = encendido). El texto de «Su pedido» de un cliente SIN la marca
  (texto libre: «ENSEÑAR A SANTY») no bloquea nada. PENDIENTE: la misma regla en la API con mensaje propio antes de llamar al procedimiento.
