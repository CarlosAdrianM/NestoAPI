using NestoAPI.Infraestructure.Kits;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;

namespace NestoAPI.Models
{
    public class ProductoDTO
    {
        public ProductoDTO()
        {
            ProductosKit = new List<ProductoKit>();
            Stocks = new List<StockProducto>();
            CategoriasSecundarias = new List<CategoriaSecundariaDTO>();
        }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public short? Tamanno { get; set; }
        public string UnidadMedida { get; set; }
        public string Familia { get; set; }
        public decimal PrecioProfesional { get; set; }
        public decimal PrecioPublicoFinal { get; set; }
        public short Estado { get; set; }
        public string Grupo { get; set; }
        // OJO con la asimetría: Grupo es el CÓDIGO ("COS") pero Subgrupo es la DESCRIPCIÓN
        // ("Cremas"). No se puede arreglar renombrando: Subgrupo viaja así en el mensaje del
        // bus y PrestaShop y Odoo lo consumen como descripción. Por eso el código del subgrupo
        // va aparte, que es lo que necesita quien tenga que identificar la categoría principal
        // (Nesto#456) en vez de adivinarla emparejando descripciones.
        public string SubgrupoCodigo { get; set; }
        public string Subgrupo { get; set; }

        // NestoAPI#423: el CÓDIGO de la familia, que es lo que guardan las tablas de Nesto
        // (DescuentosProducto.Familia, ProveedoresProducto...). `Familia`, arriba, es la
        // DESCRIPCIÓN ("Productos Genéricos"): misma asimetría que Grupo/Subgrupo y por el mismo
        // motivo — así viaja en el bus desde el principio y no se puede renombrar sin romper a
        // PrestaShop y a Odoo.
        //
        // Se añade porque la trampa ya mordió: al montar las campañas por marca, buscar filas de
        // DescuentosProducto con `dto.Familia` no habría casado NUNCA, y en silencio. Quien
        // necesite identificar la marca (una campaña, un proveedor, un filtro) usa este campo.
        public string FamiliaCodigo { get; set; }
        public string UrlEnlace { get; set; }
        public string UrlFoto { get; set; }
        public bool RoturaStockProveedor { get; set; }
        public int ClasificacionMasVendidos { get; set; }
        public string CodigoBarras { get; set; }

        // NestoAPI#421: producto que NO se vende al público. Se ve en la tienda, pero sin
        // precio ni botón de compra para quien no sea del grupo profesional. Es un dato de la
        // ficha, NO se deduce de las categorías: los subgrupos EP* (COS/EPC, APA/EXP...) son
        // categorías navegables normales y sus productos sí se venden al público.
        public bool ExclusivoProfesional { get; set; }

        // Textos editables de la tienda (PrestashopProductos), pensados para la web pero útiles
        // para cualquier consumidor. null = sin texto personalizado (el consumidor no toca nada).
        public string NombrePersonalizado { get; set; }
        public string Descripcion { get; set; }
        public string DescripcionBreve { get; set; }

        // NestoAPI#415: el tipo de IVA de la ficha (G21/R10/SR...) y su porcentaje resuelto de
        // ParametrosIVA (régimen general). Los precios viajan CON IVA y el consumidor divide para
        // almacenar la base: sin el tipo, PrestaShop creaba todo con su tax group fijo del 21 % y
        // los exentos y los del 4 % quedaban con la base mal.
        public string TipoIva { get; set; }
        public decimal? PorcentajeIva { get; set; }

        // NestoAPI#413: ofertas de tarifa hacia la web, en PORCENTAJE 0-100 y POR AUDIENCIA.
        // null = sin oferta para esa audiencia. Los precios (PrecioProfesional/PrecioPublicoFinal)
        // siguen siendo PLENOS: la tienda pinta el tachado + % (100 € −20 %, no 80 € a secas).
        // El ámbito (interno de Nesto, DescuentosProducto.AudienciaOferta) NO viaja: misma filosofía
        // que los modos de precio del cutover.
        public decimal? DescuentoPorcentajeProfesional { get; set; }
        public decimal? DescuentoPorcentajePublico { get; set; }

        public ICollection<ProductoKit> ProductosKit { get; set; }
        public ICollection<StockProducto> Stocks { get; set; }

        // NestoAPI#414: categorías comerciales SECUNDARIAS, ordenadas. Grupo/Subgrupo de la
        // ficha siguen siendo los principales; esto es la ristra adicional (Ofertas del mes,
        // Pack Regalo, Exclusivo Profesional...) que el legacy mantenía con listas a mano.
        public ICollection<CategoriaSecundariaDTO> CategoriasSecundarias { get; set; }

        public class StockProducto
        {
            public string Almacen { get; set; }
            public int Stock { get; set; }
            public int PendienteEntregar { get; set; }
            public int PendienteRecibir { get; set; }
            public int CantidadDisponible
            {
                get
                {
                    int cantidad = Stock - PendienteEntregar + PendienteReposicion;
                    return cantidad > 0 ? cantidad : 0;
                }
            }
            public DateTime FechaEstimadaRecepcion { get; set; }
            public int PendienteReposicion { get; set; }

            /// <summary>
            /// NestoAPI#412: unidades ADICIONALES del kit que se pueden montar con el stock
            /// disponible de sus componentes (el min-floor de la consulta legacy de la web).
            /// Siempre 0 en productos que no son kits.
            ///
            /// Va SEPARADO a propósito: <see cref="CantidadDisponible"/> es siempre stock físico
            /// real y ningún consumidor debe verlo inflado. El que quiera vender los montables
            /// (PrestaShop, como hacía la consulta legacy) los suma él; el que no (Odoo, que
            /// modelará el kit como BoM), lo ignora. El mismo físico de un componente puede
            /// respaldar a la vez su venta suelta y este derivado: doble conteo asumido, igual
            /// que en el legacy (repisado diario + Nesto como red de seguridad al servir).
            /// </summary>
            public int CantidadMontable { get; set; }
        }

        public static async Task<string> RutaImagen(string productoStock)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri("http://www.productosdeesteticaypeluqueriaprofesional.com/imagenesPorReferencia.php");
                client.DefaultRequestHeaders.Accept.Clear();
                //client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                try
                {
                    string parametros = "?producto=" + productoStock;
                    HttpResponseMessage response = await client.GetAsync(parametros);

                    string rutaImagen = "";
                    if (response.IsSuccessStatusCode)
                    {
                        rutaImagen = await response.Content.ReadAsStringAsync();
                        rutaImagen = "https://" + rutaImagen;
                    }

                    return rutaImagen;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public static async Task<string> RutaEnlace(string producto)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri("http://www.productosdeesteticaypeluqueriaprofesional.com/enlacePorReferencia.php");
                client.DefaultRequestHeaders.Accept.Clear();
                try
                {
                    string parametros = "?producto=" + producto;
                    HttpResponseMessage response = await client.GetAsync(parametros).ConfigureAwait(false);


                    string rutaEnlace = string.Empty;
                    if (response.IsSuccessStatusCode)
                    {
                        rutaEnlace = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        rutaEnlace += "?utm_source=nuevavision&utm_campaign=nesto";
                    }

                    return rutaEnlace;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Precio público con IVA de un producto. NestoAPI es EL DUEÑO de este cálculo (decidido el
        /// 26/08/2026 en el cutover de precios con el módulo NestoSync 1.4.0): ya no se le pregunta
        /// nada a la API de PrestaShop, que era una llamada circular —le publicábamos un precio que
        /// nos había dado la propia tienda— y una HTTP por producto en cada sincronización.
        ///
        /// El campo <c>PrestashopProductos.PVP_IVA_Incluido</c> guarda la INTENCIÓN (interna de
        /// Nesto, no viaja por el bus):
        ///   · positivo → precio público fijado a mano, se sirve tal cual
        ///   · NULL     → el público se deriva del PVP con el descuento por defecto (30 %)
        ///   · -1       → público = profesional (sentinel PVP_IVA_MISMO_QUE_PROFESIONAL)
        /// </summary>
        public static async Task<decimal> LeerPrecioPublicoFinal(string producto, NVEntities db)
        {
            var prestashopProducto = await db.PrestashopProductos
                .FirstOrDefaultAsync(pp => pp.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && pp.Número == producto)
                .ConfigureAwait(false);

            decimal? fijadoAMano = ResolverPrecioPublicoFinal(prestashopProducto?.PVP_IVA_Incluido,
                prestashopProducto?.VistoBueno);
            if (fijadoAMano.HasValue)
            {
                return fijadoAMano.Value;
            }

            return await CalcularPrecioPublicoEnLocal(producto, db, prestashopProducto?.PVP_IVA_Incluido).ConfigureAwait(false);
        }

        /// <summary>
        /// Precio público calculado desde el PVP: público = PVP / 0,7 × (1 + IVA), o
        /// PVP × (1 + IVA) si el producto está marcado con el sentinel -1.
        ///
        /// El IVA sale de ParametrosIVA cruzando el tipo del producto con el del cliente de venta
        /// en tienda (régimen general), NO del atajo "1,10 si R10, si no 1,21" que usan otros
        /// puntos del código: hay 82 productos vivos exentos y 4 al 4 % que ese atajo inflaría
        /// hasta un 21 %.
        /// </summary>
        private static async Task<decimal> CalcularPrecioPublicoEnLocal(string producto, NVEntities db, decimal? pvpIvaIncluido)
        {
            Producto ficha = await db.Productos
                .FirstOrDefaultAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == producto)
                .ConfigureAwait(false);
            if (ficha?.PVP == null || ficha.PVP <= 0)
            {
                return 0;   // sin PVP no hay nada que calcular (producto a medio dar de alta)
            }

            decimal porcentajeIva = await LeerPorcentajeIvaProducto(db, ficha.IVA_Repercutido).ConfigureAwait(false);

            // El modo del producto manda: con el sentinel -1 el público es el profesional, sin el
            // 30 %. Si no se mirara, un producto de "mismo precio" saldría un 42,86 % más caro.
            //
            // NestoAPI#406: la familia también manda, y hace falta AQUÍ y no solo en el job
            // nocturno. Un producto se crea en Nesto y el trigger trgProductosIns lo encola en
            // Nesto_sync en el acto, así que se publica a los cinco minutos, cuando todavía no
            // tiene ficha en PrestashopProductos y por tanto no tiene sentinel. Sin esta línea
            // saldría a la venta un 42,86 % más caro hasta que el job pasara de madrugada — y en
            // estas marcas (Weelko, Staleks...) el stock 0 no impide comprar, porque van a sobre
            // pedido. El job sigue haciendo falta para dejar el dato escrito, que es lo que leen
            // Nesto, NestoApp y la tienda directamente de la base de datos.
            bool mismoQueProfesional = pvpIvaIncluido == Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL
                || await LaFamiliaVendeAlPublicoComoAlProfesional(db, ficha.Familia).ConfigureAwait(false);
            return CalcularPrecioPublicoDesdePvp(ficha.PVP.Value, porcentajeIva, mismoQueProfesional);
        }

        /// <summary>
        /// NestoAPI#406: ¿la familia del producto se vende al público al mismo precio que al
        /// profesional? La regla vive en <c>Familias.PublicoIgualQueProfesional</c>, para que
        /// sumar una marca nueva sea marcar su familia y no tocar código.
        /// </summary>
        internal static async Task<bool> LaFamiliaVendeAlPublicoComoAlProfesional(NVEntities db, string familia)
        {
            if (string.IsNullOrWhiteSpace(familia))
            {
                return false;
            }

            return await db.Familias
                .AnyAsync(f => f.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                            && f.Número == familia
                            && f.PublicoIgualQueProfesional)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Carga en el DTO los textos editables de la tienda (nombre personalizado y
        /// descripciones) desde PrestashopProductos. Desde el cutover del 26/08/2026 estos textos
        /// viajan DENTRO del mensaje de Productos (el mensaje de tabla PrestashopProductos se
        /// retiró), así que hay que llamarla en TODOS los caminos que publiquen el producto.
        /// Semántica para los consumidores: null = sin personalización, no tocar el texto que
        /// tenga la tienda.
        /// </summary>
        internal static async Task CargarTextosTienda(ProductoDTO dto, NVEntities db)
        {
            PrestashopProducto fila = await db.PrestashopProductos
                .FirstOrDefaultAsync(pp => pp.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && pp.Número == dto.Producto)
                .ConfigureAwait(false);
            if (fila == null)
            {
                return;
            }

            // NestoAPI#411: sin VistoBueno los textos no viajan (quedan null = "no tocar lo que
            // tenga la tienda"). Es la puerta de publicación del proceso legacy: un texto a medio
            // escribir se queda en casa hasta que alguien lo apruebe en la pestaña Revisar.
            if (fila.VistoBueno != true)
            {
                return;
            }

            dto.NombrePersonalizado = string.IsNullOrWhiteSpace(fila.Nombre) ? null : fila.Nombre.Trim();
            dto.Descripcion = fila.Descripción;
            dto.DescripcionBreve = fila.DescripciónBreve;
        }

        /// <summary>
        /// NestoAPI#414: carga las categorías secundarias del producto, en orden. Igual que
        /// CargarTextosTienda, hay que llamarla en TODOS los caminos que publiquen el producto.
        /// Sin filas, la lista queda vacía (= el producto no tiene secundarias; los consumidores
        /// pueden retirar las que sobren SIN tocar la categoría principal).
        /// </summary>
        /// <summary>
        /// NestoAPI#422: EL constructor del DTO que se publica por el bus. Antes este bloque estaba
        /// copiado en los CINCO sitios que publican un producto (la pasada de sincronización, el
        /// job de Hangfire, la publicación manual, la masiva y la de los textos de tienda), así que
        /// cada campo nuevo del mensaje había que añadirlo cinco veces y olvidarse de uno hacía que
        /// un canal publicara datos distintos de otro, sin que nada lo detectara.
        ///
        /// Las tres diferencias que parecía haber entre los sitios resultaron no serlo:
        ///   - Los almacenes: ALMACEN_POR_DEFECTO/ALMACEN_TIENDA y ALGETE/REINA son las mismas
        ///     tres constantes ("ALG"/"REI"/"ALC") escritas de dos maneras.
        ///   - El masivo parecía no mirar Ficticio al añadir stocks, pero ya los excluye en su
        ///     propia consulta, así que ningún ficticio llega ahí.
        ///   - TieneDatosMinimosParaSincronizar es un filtro PREVIO del job, no parte de construir
        ///     el DTO: se queda en su sitio.
        ///
        /// OJO con PVP y Estado: se castean, no se hace ?? 0. Si el producto no los tiene, esto
        /// LANZA, que es lo que hacía antes y lo que queremos: mejor que falle la publicación a
        /// que la tienda reciba un precio de 0.
        ///
        /// La ficha (GetProducto con fichaCompleta) NO usa esto a propósito: es una lectura, no una
        /// publicación, y llena menos cosas según el flag.
        /// </summary>
        internal static async Task<ProductoDTO> ConstruirParaPublicar(Producto producto, NVEntities db, IProductoService productoService)
        {
            string productoId = producto.Número?.Trim();

            ProductoDTO dto = new ProductoDTO
            {
                UrlFoto = await RutaImagen(productoId).ConfigureAwait(false),
                PrecioPublicoFinal = await LeerPrecioPublicoFinal(productoId, db).ConfigureAwait(false),
                UrlEnlace = await RutaEnlace(productoId).ConfigureAwait(false),
                Producto = productoId,
                Nombre = producto.Nombre?.Trim(),
                Tamanno = producto.Tamaño,
                UnidadMedida = producto.UnidadMedida?.Trim(),
                Familia = producto.Familia1?.Descripción?.Trim(),
                FamiliaCodigo = producto.Familia?.Trim(),
                PrecioProfesional = (decimal)producto.PVP,
                Estado = (short)producto.Estado,
                Grupo = producto.Grupo,
                SubgrupoCodigo = producto.SubGrupo?.Trim(),
                Subgrupo = producto.SubGruposProducto?.Descripción?.Trim(),
                RoturaStockProveedor = producto.RoturaStockProveedor,
                ExclusivoProfesional = producto.ExclusivoProfesional,
                CodigoBarras = producto.CodBarras?.Trim()
            };

            await CargarTextosTienda(dto, db).ConfigureAwait(false);
            await CargarTipoIva(dto, db, producto.IVA_Repercutido).ConfigureAwait(false);
            await CargarCategoriasSecundarias(dto, db).ConfigureAwait(false);
            // #423: familia y grupo salen de la FICHA, sin recortar, porque DescuentosProducto.Familia
            // es char(10) igual que Productos.Familia y la comparación en SQL casa con el relleno.
            // Lo que NO vale es tirar de `dto.Familia`: ahí va la DESCRIPCIÓN ("Productos Genéricos"),
            // no el código, y no casaría con ninguna fila de descuento — en silencio. Para quien
            // necesite el código desde el DTO está `dto.FamiliaCodigo`.
            await CargarDescuentosPorAudiencia(dto, db, producto.PVP, producto.Familia, producto.Grupo).ConfigureAwait(false);

            foreach (Kit kit in producto.Kits)
            {
                dto.ProductosKit.Add(new ProductoKit
                {
                    ProductoId = kit.NúmeroAsociado.Trim(),
                    Cantidad = kit.Cantidad
                });
            }

            if (!producto.Ficticio && productoService != null)
            {
                dto.Stocks.Add(await productoService.CalcularStockProducto(productoId, Constantes.Productos.ALMACEN_POR_DEFECTO).ConfigureAwait(false));
                dto.Stocks.Add(await productoService.CalcularStockProducto(productoId, Constantes.Productos.ALMACEN_TIENDA).ConfigureAwait(false));
                dto.Stocks.Add(await productoService.CalcularStockProducto(productoId, Constantes.Almacenes.ALCOBENDAS).ConfigureAwait(false));
            }

            return dto;
        }

        internal static async Task CargarCategoriasSecundarias(ProductoDTO dto, NVEntities db)
        {
            var filas = await db.ProductosCategoriasSecundarias
                .Where(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && c.Número == dto.Producto)
                .OrderBy(c => c.Orden)
                .Select(c => new
                {
                    c.Grupo,
                    c.SubGrupo,
                    DescripcionSubgrupo = c.SubGruposProducto.Descripción,
                    DescripcionGrupo = db.GruposProductoes
                        .Where(g => g.Empresa == c.Empresa && g.Número == c.Grupo)
                        .Select(g => g.Descripción)
                        .FirstOrDefault()
                })
                .ToListAsync().ConfigureAwait(false);

            dto.CategoriasSecundarias = filas.Select(f => new CategoriaSecundariaDTO
            {
                Grupo = f.Grupo?.Trim(),
                DescripcionGrupo = f.DescripcionGrupo?.Trim(),
                Subgrupo = f.SubGrupo?.Trim(),
                DescripcionSubgrupo = f.DescripcionSubgrupo?.Trim()
            }).ToList();
        }

        /// <summary>
        /// NestoAPI#413: carga los descuentos de tarifa hacia la web. Igual que CargarTextosTienda,
        /// hay que llamarla en TODOS los caminos que publiquen el producto. Filtros del proceso
        /// legacy (pasos 5-7): filas de TARIFA (sin cliente ni proveedor), CantidadMínima menor
        /// que 2, y desde #413 además AudienciaOferta mayor que 0 (el 0, default, es "no va a la web").
        ///
        /// Desde #423 se filtra además por vigencia, con la MISMA regla que el motor de precios
        /// (<see cref="Infraestructure.Vigencia"/>): la tienda no puede anunciar un
        /// descuento que Nesto ya no cobraría. OJO: una campaña que caduca por fecha no modifica
        /// ninguna fila, así que no basta con esto para que la oferta desaparezca de la tienda —
        /// hace falta que algo reencole el producto en Nesto_sync (el job del Slice 2 de #423).
        /// </summary>
        internal static async Task CargarDescuentosPorAudiencia(ProductoDTO dto, NVEntities db, decimal? pvp,
            string familia = null, string grupo = null)
        {
            // #423 (Slice 3): además de las filas del producto, las de su FAMILIA. Son los dos
            // niveles de tarifa que el motor de precios aplica de verdad (ver la precedencia en
            // CalcularDescuentosPorAudiencia); una campaña de marca es una fila, no cuarenta.
            //
            // FiltroProducto fuera: el motor solo aplica esas filas junto a un cliente concreto
            // (niveles 8 y 9), así que en tarifa pura no valen para nadie y publicarlas anunciaría
            // un descuento que Nesto no cobra.
            System.Collections.Generic.List<DescuentosProducto> filas = await Infraestructure.Vigencia.Vigentes(db.DescuentosProductoes)
                .Where(d => d.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                    && (d.Nº_Producto == dto.Producto || (familia != null && d.Familia == familia))
                    && (d.Nº_Cliente == null || d.Nº_Cliente.Trim() == string.Empty)
                    && (d.NºProveedor == null || d.NºProveedor.Trim() == string.Empty)
                    && d.FiltroProducto == null
                    && d.CantidadMínima < 2
                    && d.AudienciaOferta > 0)
                .ToListAsync().ConfigureAwait(false);

            DescuentosPorAudiencia calculados = CalcularDescuentosPorAudiencia(filas, pvp, grupo);
            dto.DescuentoPorcentajeProfesional = calculados.Profesional;
            dto.DescuentoPorcentajePublico = calculados.Publico;
        }

        /// <summary>
        /// NestoAPI#413: del conjunto de filas de tarifa YA FILTRADAS deduce el % por audiencia.
        /// El % de cada fila sale de Descuento (0,20 = 20 %) o, si la fila lleva Precio fijo, se
        /// deriva contra el PVP como hacía el paso 7 del legacy (1 − Precio/PVP). Ámbitos:
        /// 1 = solo profesionales, 2 = ambos (el público usa DescuentoPublico si está, si no el
        /// mismo %), 3 = solo público.
        ///
        /// NestoAPI#423 (Slice 4): el 3 está PROHIBIDO en la base de datos
        /// (<c>CK_DescuentosProducto_Audiencia</c>). GestorPrecios no mira la audiencia, así que
        /// una fila "solo público" seguiría descontándole al profesional en el pedido: la tienda
        /// diría una cosa y Nesto cobraría otra. El código de aquí sigue sabiendo tratarlo porque
        /// es la definición de la semántica, y para que el día que el motor respete la audiencia
        /// baste con retirar la restricción. Con ella puesta no puede llegar ninguna fila así.
        ///
        /// NestoAPI#423 (Slice 3): con filas de varios NIVELES ya no vale "gana el mayor". Se
        /// replica la precedencia EXACTA de <c>GestorPrecios.calcularDescuentoProducto</c> para un
        /// cliente sin filas propias, que es lo que la tienda debe anunciar:
        ///
        ///   1. familia            → fija el %
        ///   2. familia + grupo    → SOBRESCRIBE al anterior (aunque sea menor)
        ///   3. producto           → gana solo si es MAYOR que lo acumulado
        ///
        /// Los pasos 1 y 2 sobrescriben porque en el motor son asignaciones directas; el 3 lleva
        /// una comparación `>`. Copiar esa asimetría es lo que hace que la tienda no anuncie un
        /// porcentaje distinto del que Nesto acaba cobrando.
        ///
        /// Dentro de un mismo nivel gana la de mayor CantidadMínima, igual que el motor (que
        /// ordena por CantidadMínima descendente), NO la del % mayor. Antes se cogía el máximo:
        /// con dos filas de CantidadMínima 0 y 1 la tienda podía anunciar un porcentaje que el
        /// pedido de una unidad no aplicaba.
        ///
        /// El Precio fijo solo se deriva a % en el nivel de PRODUCTO: el motor nunca lee el
        /// Precio de una fila de familia (los niveles de familia solo miran Descuento), y
        /// repartir un precio fijo entre los productos de una marca no significaría nada.
        /// </summary>
        internal static DescuentosPorAudiencia CalcularDescuentosPorAudiencia(
            System.Collections.Generic.IEnumerable<DescuentosProducto> filas, decimal? pvp, string grupo = null)
        {
            DescuentosPorAudiencia resultado = new DescuentosPorAudiencia();
            if (filas == null)
            {
                return resultado;
            }

            System.Collections.Generic.List<DescuentosProducto> lista = filas.ToList();

            // Cada audiencia se calcula por separado, como si el motor corriera dos veces: primero
            // sobre lo que se le publica al profesional y luego sobre lo que se le publica al
            // público. Así "25 % a profesionales y 10 % al público" se puede seguir expresando con
            // dos filas de ámbitos distintos (lo de #413), y no solo con DescuentoPublico.
            resultado.Profesional = CalcularParaAudiencia(lista, pvp, grupo,
                f => f.AudienciaOferta == 1 || f.AudienciaOferta == 2, usarDescuentoPublico: false);
            resultado.Publico = CalcularParaAudiencia(lista, pvp, grupo,
                f => f.AudienciaOferta == 2 || f.AudienciaOferta == 3, usarDescuentoPublico: true);

            return resultado;
        }

        private static decimal? CalcularParaAudiencia(System.Collections.Generic.List<DescuentosProducto> lista,
            decimal? pvp, string grupo, Func<DescuentosProducto, bool> esDeLaAudiencia, bool usarDescuentoPublico)
        {
            System.Collections.Generic.List<DescuentosProducto> suyas = lista.Where(esDeLaAudiencia).ToList();

            // La clasificación es por la FORMA de la fila, igual que los filtros del motor.
            DescuentosProducto deFamilia = MejorDelNivel(suyas.Where(f => f.Familia != null && f.GrupoProducto == null));
            DescuentosProducto deFamiliaYGrupo = MejorDelNivel(suyas.Where(f => f.Familia != null && f.GrupoProducto != null
                && grupo != null && f.GrupoProducto.Trim() == grupo.Trim()));
            DescuentosProducto deProducto = MejorDelNivel(suyas.Where(f => f.Familia == null && f.GrupoProducto == null));

            // Se acumula en decimal (no en decimal?) para que un nivel con Descuento 0 pueda
            // ANULAR al anterior, igual que en el motor: "toda la marca al 20 %, menos su línea de
            // peluquería" es una fila de familia+grupo al 0 %. Al final, 0 vuelve a ser "sin
            // oferta" (null), que es lo que la tienda necesita para no pintar el tachado.
            decimal porcentaje = 0M;

            if (deFamilia != null)
            {
                porcentaje = PorcentajeDe(deFamilia, pvp, derivarDePrecio: false, usarDescuentoPublico);
            }
            if (deFamiliaYGrupo != null)
            {
                porcentaje = PorcentajeDe(deFamiliaYGrupo, pvp, derivarDePrecio: false, usarDescuentoPublico);
            }
            if (deProducto != null)
            {
                // El nivel de producto lleva `>` en el motor, no una asignación: no pisa a la
                // familia si es menor.
                porcentaje = Math.Max(porcentaje, PorcentajeDe(deProducto, pvp, derivarDePrecio: true, usarDescuentoPublico));
            }

            return porcentaje > 0M ? porcentaje : (decimal?)null;
        }

        /// <summary>
        /// De las filas de un mismo nivel, la que aplicaría el motor: la de mayor CantidadMínima
        /// (que ordena descendente y coge la primera). Si hubiera dos iguales es un duplicado de
        /// los de #229 y el motor ya lo denuncia al calcular el precio; aquí se coge una y no se
        /// revienta la publicación del producto por un error de datos.
        /// </summary>
        private static DescuentosProducto MejorDelNivel(System.Collections.Generic.IEnumerable<DescuentosProducto> delNivel)
        {
            return delNivel.OrderByDescending(f => f.CantidadMínima).FirstOrDefault();
        }

        private static decimal PorcentajeDe(DescuentosProducto fila, decimal? pvp, bool derivarDePrecio, bool usarDescuentoPublico)
        {
            if (usarDescuentoPublico && fila.DescuentoPublico.HasValue)
            {
                return Math.Round(fila.DescuentoPublico.Value * 100M, 2);
            }

            if (fila.Descuento > 0)
            {
                return Math.Round(fila.Descuento * 100M, 2);
            }

            if (derivarDePrecio && fila.Precio > 0 && pvp > 0)
            {
                decimal derivado = Math.Round((1M - (fila.Precio.Value / pvp.Value)) * 100M, 2);
                if (derivado > 0)
                {
                    return derivado; // un Precio fijo POR ENCIMA del PVP no es una oferta
                }
            }

            return 0M;
        }

        /// <summary>
        /// NestoAPI#415: carga en el DTO el tipo de IVA de la ficha y su porcentaje. Igual que
        /// CargarTextosTienda, hay que llamarla en TODOS los caminos que publiquen el producto.
        /// </summary>
        internal static async Task CargarTipoIva(ProductoDTO dto, NVEntities db, string ivaRepercutido)
        {
            dto.TipoIva = ivaRepercutido?.Trim();
            dto.PorcentajeIva = await LeerPorcentajeIvaProducto(db, ivaRepercutido).ConfigureAwait(false);
        }

        /// <summary>
        /// Porcentaje de IVA repercutido de un producto para el cliente de venta en tienda
        /// (régimen general). Si el tipo del producto no está en ParametrosIVA, el general: es el
        /// que llevan 7.264 de los 7.356 productos vivos, y equivocarse al alza nunca regala nada.
        /// </summary>
        internal static async Task<decimal> LeerPorcentajeIvaProducto(NVEntities db, string ivaRepercutido)
        {
            decimal? porcentajeIva = await db.ParametrosIVA
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                    && p.IVA_Producto == ivaRepercutido
                    && p.IVA_Cliente_Prov == Constantes.Empresas.IVA_POR_DEFECTO)
                .Select(p => (decimal?)p.C__IVA)
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            return porcentajeIva ?? PORCENTAJE_IVA_POR_DEFECTO;
        }

        // Si el producto tiene un tipo de IVA que no está en ParametrosIVA, el general: es el que
        // llevan 7.264 de los 7.356 productos vivos, y equivocarse al alza nunca regala nada.
        private const decimal PORCENTAJE_IVA_POR_DEFECTO = 21M;

        /// <summary>
        /// Público = PVP / 0,7 × (1 + IVA) en el caso normal, y PVP × (1 + IVA) cuando el producto
        /// está marcado como "mismo precio que el profesional" (sentinel -1).
        ///
        /// Redondeo AwayFromZero, como el resto de la casa: es el HALF_UP de PrestaShop
        /// (PS_PRICE_ROUND_MODE), así que el céntimo coincide con el que muestra la web.
        /// </summary>
        internal static decimal CalcularPrecioPublicoDesdePvp(decimal pvp, decimal porcentajeIva,
            bool mismoQueProfesional = false)
        {
            decimal publicoSinIva = mismoQueProfesional
                ? pvp
                : pvp / Constantes.Productos.FACTOR_PRECIO_PROFESIONAL;
            return Math.Round(publicoSinIva * (1 + porcentajeIva / 100), 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Decide si el campo <c>PVP_IVA_Incluido</c> de PrestashopProductos ES un precio o es una
        /// intención. Devuelve el precio cuando lo es; <c>null</c> cuando hay que calcularlo del PVP
        /// (modos NULL y -1, ver <see cref="Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL"/>).
        ///
        /// Cualquier valor que no sea un precio (el 0, o un negativo) cae al cálculo: antes bastaba
        /// con ser distinto de 0 para viajar como precio, y un -1 habría salido como precio público
        /// — y en la plantilla de PUBLICO_FINAL, dividido por el IVA, como precio de venta NEGATIVO.
        /// </summary>
        internal static decimal? ResolverPrecioPublicoFinal(decimal? pvpIvaIncluido)
        {
            return pvpIvaIncluido > 0 ? pvpIvaIncluido : null;
        }

        /// <summary>
        /// NestoAPI#411: un precio FIJO solo se sirve con VistoBueno — sin revisar, se cae al
        /// modo derivado (30 %), que no necesita revisión. La puerta NO toca los modos NULL y -1:
        /// los pone un proceso deliberado (la pantalla o el script del sentinel del cutover, cuyas
        /// filas tienen VistoBueno NULL), y este método ya los devuelve como "no es un precio"
        /// antes de mirar el visto bueno.
        /// </summary>
        internal static decimal? ResolverPrecioPublicoFinal(decimal? pvpIvaIncluido, bool? vistoBueno)
        {
            return vistoBueno == true ? ResolverPrecioPublicoFinal(pvpIvaIncluido) : null;
        }

        /// <summary>
        /// Margen para dar dos precios por iguales al comparar los nuestros con los que llegan de
        /// fuera (PrestaShop redondea en PHP, nosotros en C#: el céntimo puede bailar). Dos céntimos,
        /// decidido el 26/08/2026.
        /// </summary>
        internal const decimal TOLERANCIA_IGUALDAD_PRECIOS = 0.02M;

        /// <summary>
        /// La operación inversa a <see cref="LeerPrecioPublicoFinal"/>: cuando un sistema externo
        /// (PrestaShop, Odoo) publica un producto con su precio público, deduce QUÉ INTENCIÓN hay
        /// detrás y devuelve lo que debe guardarse en <c>PrestashopProductos.PVP_IVA_Incluido</c>:
        ///
        ///   · público ≈ PVP / 0,7 × (1+IVA) → NULL (sigue la regla general del 30 %)
        ///   · público ≈ PVP × (1+IVA)       → -1  (mismo precio que el profesional)
        ///   · cualquier otra cosa           → el propio público (precio fijado a mano)
        ///
        /// Guardar la intención en vez del número es lo que hace que el público se recalcule solo
        /// cuando cambie el PVP. El coste asumido: un precio fijado a mano que coincida al céntimo
        /// con una de las fórmulas se guardará como intención y se moverá con el PVP. En ~10.000
        /// referencias pasará alguna vez; el siguiente mensaje de la tienda lo recoloca.
        /// </summary>
        internal static decimal? InferirModoPrecioPublico(decimal publicoConIva, decimal pvp, decimal porcentajeIva)
        {
            decimal derivado = CalcularPrecioPublicoDesdePvp(pvp, porcentajeIva);
            if (Math.Abs(publicoConIva - derivado) <= TOLERANCIA_IGUALDAD_PRECIOS)
            {
                return null;
            }

            decimal profesionalConIva = CalcularPrecioPublicoDesdePvp(pvp, porcentajeIva, mismoQueProfesional: true);
            if (Math.Abs(publicoConIva - profesionalConIva) <= TOLERANCIA_IGUALDAD_PRECIOS)
            {
                return Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL;
            }

            return publicoConIva;
        }

        /// <summary>
        /// Obtiene la URL del producto en la tienda online usando la API de Prestashop.
        /// Busca el producto por su referencia y construye la URL amigable.
        /// Issue #74: Sistema de correos post-compra.
        /// </summary>
        /// <param name="producto">Referencia del producto (ej: "12345")</param>
        /// <returns>URL completa del producto en la tienda o null si no existe</returns>
        public static async Task<string> LeerUrlTiendaOnline(string producto)
        {
            if (string.IsNullOrWhiteSpace(producto))
            {
                return null;
            }

            string urlPrestashop = $"http://www.productosdeesteticaypeluqueriaprofesional.com/api/products?filter[reference]={producto.Trim()}";
            string userName;

            try
            {
                userName = ConfigurationManager.AppSettings["PrestashopWebserviceKeyNV"];
            }
            catch
            {
                return null;
            }

            if (string.IsNullOrEmpty(userName))
            {
                return null;
            }

            try
            {
                using (var handler = new HttpClientHandler { Credentials = new NetworkCredential { UserName = userName } })
                using (HttpClient client = new HttpClient(handler))
                {
                    // 1. Buscar el producto por referencia
                    HttpResponseMessage response = await client.GetAsync(urlPrestashop).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    string xmlResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(xmlResponse);

                    XmlNode productNode = xmlDoc.SelectSingleNode("//product");
                    if (productNode == null)
                    {
                        return null;
                    }

                    // 2. Obtener detalles del producto para construir la URL
                    string urlProductoApi = productNode.Attributes["xlink:href"]?.Value;
                    if (string.IsNullOrEmpty(urlProductoApi))
                    {
                        return null;
                    }

                    HttpResponseMessage responseProducto = await client.GetAsync(urlProductoApi).ConfigureAwait(false);
                    if (!responseProducto.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    string xmlProducto = await responseProducto.Content.ReadAsStringAsync().ConfigureAwait(false);
                    XmlDocument xmlDocProducto = new XmlDocument();
                    xmlDocProducto.LoadXml(xmlProducto);

                    // 3. Extraer id y link_rewrite para construir la URL amigable
                    XmlNode idNode = xmlDocProducto.SelectSingleNode("//product/id");
                    XmlNode linkRewriteNode = xmlDocProducto.SelectSingleNode("//product/link_rewrite/language");

                    if (idNode == null || linkRewriteNode == null)
                    {
                        return null;
                    }

                    string idProducto = idNode.InnerText;
                    string linkRewrite = linkRewriteNode.InnerText;

                    // 4. Construir la URL amigable con parámetros UTM
                    string urlTienda = $"https://www.productosdeesteticaypeluqueriaprofesional.com/{idProducto}-{linkRewrite}.html";
                    urlTienda += "?utm_source=nuevavision&utm_medium=email&utm_campaign=postcompra";

                    return urlTienda;
                }
            }
            catch
            {
                return null;
            }
        }
    }

    public class ProductoKit
    {
        public string ProductoId { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>
    /// NestoAPI#413: resultado del cálculo de ofertas de tarifa hacia la web, en % 0-100 por
    /// audiencia (null = sin oferta para esa audiencia).
    /// </summary>
    public class DescuentosPorAudiencia
    {
        public decimal? Profesional { get; set; }
        public decimal? Publico { get; set; }
    }

    /// <summary>
    /// NestoAPI#414: una categoría secundaria de producto (par grupo/subgrupo con sus
    /// descripciones). Viaja en el mensaje de Productos en el orden definido en la pantalla.
    /// </summary>
    public class CategoriaSecundariaDTO
    {
        public string Grupo { get; set; }
        public string DescripcionGrupo { get; set; }
        public string Subgrupo { get; set; }
        public string DescripcionSubgrupo { get; set; }
    }

    public class SubgrupoProductoDTO
    {
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        public string Nombre { get; set; }
        public string GrupoSubgrupo => Grupo + Subgrupo;
    }
}