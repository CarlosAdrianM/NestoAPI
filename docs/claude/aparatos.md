# Aparatos: registro por producto y número de serie

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). El registro de cada aparato (la ventana de Alquileres de Nesto)
y su historia por número de serie (NestoAPI#913). Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca
su línea del índice. (Las etiquetas de inmovilizado están en `almacen-etiquetas.md`.)

### Historia del aparato y descuadres por número de serie (NestoAPI#913 corte 1, Carlos 26/11/26; SOLO LECTURA)
- Por qué: la columna «Descuadre» de la ventana (`prdProductosAlquilerLista`: ExtractoProducto 1+3+5 de todos los almacenes − registros de
  `CabAlquileres` sin cliente ni inmovilizado, de los 36 productos de `ProductosAlquiler`) cuenta UNIDADES, no aparatos: una venta sin apuntar y
  una compra sin registrar se compensan. El corte 1 cruza por número de serie con lo que ya hay, sin tocar nada.
- `AparatosController` (`[Authorize]`; Dirección, Informática, Administración, Almacén y Compras —`IsInRoleSinDominio`—; cliente de la tienda 403):
  - `GET api/Aparatos/Historia?producto=&numeroSerie=` (los dos obligatorios → 400) → `HistoriaAparatoDTO` {Producto, Nombre, NumeroSerie,
    Situacion (EnStock | Vendido | Inmovilizado | Alquilado | DevueltoAlProveedor | SinRastro, como texto), TextoSituacion («Vendido al cliente 22709
    (NOMBRE) el 17/11/2026, pedido 930489.»), Registro (la fila de CabAlquileres más reciente, con sus campos y `SituacionSegunRegistro`),
    OtrosRegistros (números si la serie está repetida), EnTablaNumerosSerie, Movimientos [{Fecha, Tipo Compra | DevolucionProveedor | Venta | Abono |
    Inmovilizado | RegistroAlquiler, Documento, Tercero, NombreTercero, Texto}] por fecha}. 404 de usted si no hay rastro en ningún sitio.
  - `GET api/Aparatos/Descuadres?soloListaAlquileres=false` → `InformeDescuadresAparatosDTO` {SoloListaAlquileres, TotalDescuadres, Descuadres
    [{Producto, Nombre, NumeroSerie, Motivo, TextoMotivo, Motivos, SituacionSegunRegistro, SituacionCalculada, TextoSituacion, Registros}] (solo los que
    NO coinciden; 2.000 como mucho y `Aviso`), Productos [{Producto, Nombre, StockContable [{Almacen, Cantidad}], StockContableTotal,
    EnStockSegunRegistro, EnStockSegunMovimientos, Diferencia (registro − movimientos), DiferenciaStockContable (stock − registro: la columna vieja con
    las empresas 1 y 3)}]}. Alcance: con `soloListaAlquileres`, los de `ProductosAlquiler`; si no, también los de `Productos.NecesitaNumSerie` y los
    que tienen algún registro (la lista controlada debe salir de la marca, no de una tabla aparte).
- LA REGLA (pura) `ReglaSituacionAparato` (`Infraestructure/Aparatos/Aparatos.cs`): la situación se CALCULA de los movimientos, nunca del registro.
  Manda el último por fecha (a igual fecha: compra, devolución al proveedor, venta, abono, inmovilizado; sin fecha, al principio). Compra o abono →
  en stock (vendido y después abonado vuelve al stock; la última venta manda); venta → vendido, o alquilado si el registro tiene cuotas y es del
  mismo cliente; inmovilizado VIVO → inmovilizado (uno anulado sale en la historia pero no decide); compra en negativo → devuelto al proveedor; nada →
  sin rastro (el registro solo no cuenta). Lo que dice el registro (`SegunRegistro`): con inmovilizado, inmovilizado; con cliente, alquilado (cuotas >
  0) o vendido; si no, en stock. Series y productos se comparan recortados de los extremos y sin mayúsculas (`Clave`).
- Motivos (`ReglaDescuadresAparatos.Motivos`, en este orden): SerieRepetidaEnElRegistro · RegistroEnStockPeroVendido · RegistroEnStockPeroInmovilizado
  · InmovilizadoSinEnlazar (hay registro sin inmovilizado y un inmovilizado vivo nombra la serie) · RegistroConClienteSinVenta (ninguna venta con la
  serie) · RegistroConClientePeroDevuelto (la última venta se abonó) · VendidoSinRegistro (sin registro, producto con NecesitaNumSerie y vendido)
  · CompradoSinRegistro (sin registro, producto con NecesitaNumSerie O de `ProductosAlquiler`, alguna compra recibida —LinPedidoCmp Estado ≥ 2: las -99
  no cuentan— y en stock según los movimientos; también si se vendió y después se abonó) · SerieEscritaDeDosFormas (al final, ver abajo).
- **Series escritas de dos formas (corte 1 b, 26/11/26)**: LA REGLA (pura) `ReglaSeriesParecidas.Normalizar` = mayúsculas, sin espacios (también
  interiores), guiones ni puntos, O → 0, I → 1 y sin ceros a la izquierda en CADA tramo de cifras (un tramo solo de ceros se queda en «0»):
  «SHE-RFS009-2511111» = «SHE RFS 9.2511111», «A32046» = «A32O46», «0012345» = «12345»; «A10» ≠ «A1». Solo dentro del MISMO producto y entre
  series distintas (por `Clave`). Cada una sale con el motivo (aunque coincida con su registro), `OtrasFormasDeLaSerie` (las demás, tal cual) y
  `GrupoSerie` (la forma común); en el listado salen JUNTAS, en el sitio de la primera del grupo (lo demás, en el orden de siempre). NUNCA se
  unifican solas: «compruebe cuál es la buena». `OtrasFormasDeLaSerie` (lista vacía) y `GrupoSerie` (null) son aditivos en cada descuadre.
- Movimientos (`ConstructorMovimientosAparato`): ventas = LinPedidoVta Estado ≥ 2, Cantidad ≠ 0 y NumSerie (positiva venta, negativa abono; fecha
  `[Fecha Albarán]`; documento pedido · albarán · factura); compras = LinPedidoCmp Estado ≥ 2, Cantidad ≠ 0 y NumSerie (fecha `FechaAlbarán` o, si no,
  `FechaRecepción`; proveedor `NºProveedor` con el nombre de Proveedores por empresa + número + contacto); inmovilizados = la serie ESCRITA en
  `Inmovilizados.Descripción` (`ReglaDescripcionEtiquetaInmovilizado.SerieEscrita`/`ReferenciaEscrita`, la misma lectura que la etiqueta), fecha
  `FechaCreación`: con «ref.» solo de ese producto; sin ella, si la serie es de UN único producto leído. Todas las empresas en ventas, compras,
  registros e inmovilizados; stock contable = ExtractoProducto de las empresas 1 y 3 por almacén (no se puede saber por serie).
- Lectura (`LectorAparatosSql`, ADO por `SqlQuery`, NOLOCK): una consulta por fuente (productos, registros, ventas, compras, inmovilizados,
  NumerosSerie y, en el informe, stock), todas con el mismo alcance; sin N+1. La historia filtra por producto y `UPPER(LTRIM(RTRIM(serie)))`; los
  inmovilizados, por `LIKE` de la serie (en el informe se leen todos, unos 826).
- Sin hacer (cortes siguientes de la issue): registro vendido que los movimientos contradicen en otro sentido. Los inmovilizados, en el corte 2 (abajo).

### Corte 2: inmovilizados — propuesta de enlace, enlace con ensayo y descuadres (NestoAPI#913 corte 2, Carlos 27/11/26)
- Por qué: hoy Santiago, al crear el inmovilizado de una máquina, tiene que ir a la ventana de Alquileres a teclear el número en el
  registro. El enlace sigue viviendo donde siempre, `CabAlquileres.Inmovilizado` (char(10), FK a `Inmovilizados` por Empresa + Número; lo
  mismo que escribe la ventana con `POST api/Alquileres/Cabeceras/Guardar`), pero ahora lo propone y lo escribe la API.
- `GET api/Aparatos/Inmovilizados/Propuestas` (mismos permisos que el corte 1, SOLO LECTURA) → `PropuestasInmovilizadosDTO` {TotalInmovilizados,
  Enlazados (algún registro los pone), Anulados, SinEnlazarConSerie, SinEnlazarSinSerie (no se proponen: mobiliario, informática…),
  ConCertezaAlta, ColumnasEnInmovilizados, Propuestas [{Empresa, Inmovilizado, Codigo «I/1824», Descripcion, FechaCreacion, SerieLeida,
  ReferenciaLeida, OrigenSerie Texto | Columna, Certeza UnaCoincidenciaExacta | VariasCoincidencias | SoloPorParecido | Ninguna, CertezaAlta,
  TextoCerteza, Candidatos [{Producto, Nombre, NumeroSerie, Exacta, Registros, SituacionCalculada, Aplicable, Motivo}], RegistroPropuesto,
  MotivoNoAplicable}], AparatosSinInmovilizado [{Empresa, Registro, Producto, Nombre, NumeroSerie, InmovilizadoDelRegistro, Motivo FichaNoExiste
  | FichaAnulada | FichaConOtraSerie, Texto, InmovilizadoPropuesto}]}. Solo inmovilizados VIVOS y sin enlazar.
- `POST api/Aparatos/Inmovilizados/Enlazar?ensayo=true` (por defecto ENSAYO: dice qué haría; `ensayo=false` escribe) con
  `{ "TodasLasDeCertezaAlta": true }` (en bloque, solo las de certeza alta) y/o `{ "Pares": [{ "Empresa": "1", "Inmovilizado": "I/1824",
  "Registro": 1242 }] }` (uno a uno; sin `Registro`, el de la propuesta solo si es de certeza alta; con él, el par A MANO aunque solo se
  parezca). 400 sin nada o con más de 500 pares. → `ResultadoEnlaceInmovilizadosDTO` {Ensayo, Enlazados, EscribeColumnasEnInmovilizados,
  Filas [{Empresa, Inmovilizado, Codigo, Registro, Producto, NumeroSerie, Origen CertezaAlta | AMano, Ok, Motivo}]}. Un par no se enlaza si el
  inmovilizado no existe, está anulado o ya está en otro registro, o si el registro no existe, es de otra empresa o ya tiene otro; ni un
  inmovilizado ni un registro dos veces en la misma petición (la primera fila manda).
- Escritura (`EscritorEnlacesInmovilizadoSql`), una transacción por enlace: `UPDATE CabAlquileres SET Inmovilizado, Usuario
  (UsuarioAuditoriaHelper.Resolver), FechaModificación` SOLO si el registro sigue como se leyó (mismo producto y serie, sin inmovilizado) y el
  inmovilizado sigue vivo y en ningún otro registro (si no, la fila sale «ha cambiado mientras tanto»); rastro en `Modificaciones` (Tabla
  «CabAlquileres», Anterior/Nuevo en JSON con el registro, el inmovilizado y el aparato); con el script, también `Inmovilizados.Producto` y
  `.NumeroSerie` si están vacías (por `sp_executesql` tras `COL_LENGTH`: sin las columnas el lote compila igual).
- LA REGLA de la serie escrita (pura, `ReglaSerieInmovilizado.Leer`): primero la de la etiqueta (`SerieEscrita`: «n/s», «s/n», «nº serie», «nº de
  serie», «no. serie», etiqueta separada de la serie); si no, una más amplia: la etiqueta pegada («n/s12345», «S/N:AB-1»), «número de serie»,
  «núm. serie», «num serie». Quita «:», «#», «=», «-» de delante y la puntuación final. Con texto detrás («n/s 12345 (demo)») se prueba la
  entera y su PRIMER TRAMO («12345»). «Serie 5000» a secas NO es serie (puede ser el modelo). La referencia, `ReferenciaEscrita` («ref.44276»).
  Con las columnas del script rellenas, mandan ellas (`DeFicha`). El corte 1 (movimientos de la historia) usa la misma lectura.
- LA REGLA de la propuesta (pura, `ReglaPropuestasInmovilizados`): aparatos conocidos = producto + serie de registros, compras, ventas y
  NumerosSerie (un inmovilizado solo no hace aparato). Exacta = `Clave` igual (con referencia escrita, solo ese producto); si no, parecida: la
  referencia escrita es de otro producto, `ReglaSeriesParecidas.Normalizar` igual, o la descripción está llena (50 caracteres, char(50)) y la
  serie de un aparato empieza igual (cortada). Certeza ALTA = una sola exacta, con un solo registro, de la misma empresa, sin inmovilizado, y
  ningún otro inmovilizado vivo propone el mismo registro. Los números de inmovilizado se comparan sin relleno ni ceros a la izquierda.
- Descuadres nuevos en `GET api/Aparatos/Descuadres` (aditivos; `Inmovilizado` «I/1824» nuevo en cada fila, null si no aplica):
  `InmovilizadoSinFicha` (el registro está «inmovilizado» y la ficha no existe o está anulada) y `InmovilizadoSinAparato` (ficha viva, sin
  enlazar, con serie de ningún aparato; al final del listado, por número; solo con `soloListaAlquileres=false`, porque con la lista no se leen
  los demás productos).
- Script OPCIONAL `Scripts/Issue913b_Inmovilizados_ProductoYNumeroSerie.sql` (sa): `Inmovilizados.Producto char(15)` y `.NumeroSerie
  varchar(30)` NULL + GRANT UPDATE en Inmovilizados (verbo nuevo). Sin él todo funciona leyendo el texto. Antes, comprobar que nada inserta en
  Inmovilizados sin lista de columnas. El EDMX NO se toca (se lee y escribe por SQL).
- Sin hacer: el enlace AL CREAR el inmovilizado (el alta no está en la API ni en el Nesto nuevo; cuando pase, que elija el aparato y escriba
  las columnas), la pantalla en Nesto, y que el Guardar de la ventana no pise un enlace puesto por la API desde una copia vieja.
