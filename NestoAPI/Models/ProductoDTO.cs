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
        public string Subgrupo { get; set; }
        public string UrlEnlace { get; set; }
        public string UrlFoto { get; set; }
        public bool RoturaStockProveedor { get; set; }
        public int ClasificacionMasVendidos { get; set; }
        public string CodigoBarras { get; set; }

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
        // El ámbito (interno de Nesto, DescuentosProducto.AmbitoWeb) NO viaja: misma filosofía
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
            bool mismoQueProfesional = pvpIvaIncluido == Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL;
            return CalcularPrecioPublicoDesdePvp(ficha.PVP.Value, porcentajeIva, mismoQueProfesional);
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
        /// que 2, y desde #413 además AmbitoWeb mayor que 0 (el 0, default, es "no va a la web").
        /// </summary>
        internal static async Task CargarDescuentosWeb(ProductoDTO dto, NVEntities db, decimal? pvp)
        {
            System.Collections.Generic.List<DescuentosProducto> filas = await db.DescuentosProductoes
                .Where(d => d.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                    && d.Nº_Producto == dto.Producto
                    && (d.Nº_Cliente == null || d.Nº_Cliente.Trim() == string.Empty)
                    && (d.NºProveedor == null || d.NºProveedor.Trim() == string.Empty)
                    && d.CantidadMínima < 2
                    && d.AmbitoWeb > 0)
                .ToListAsync().ConfigureAwait(false);

            DescuentosWebCalculados calculados = CalcularDescuentosWeb(filas, pvp);
            dto.DescuentoPorcentajeProfesional = calculados.Profesional;
            dto.DescuentoPorcentajePublico = calculados.Publico;
        }

        /// <summary>
        /// NestoAPI#413: del conjunto de filas de tarifa YA FILTRADAS deduce el % por audiencia.
        /// El % de cada fila sale de Descuento (0,20 = 20 %) o, si la fila lleva Precio fijo, se
        /// deriva contra el PVP como hacía el paso 7 del legacy (1 − Precio/PVP). Ámbitos:
        /// 1 = solo profesionales, 2 = ambos (el público usa DescuentoPublico si está, si no el
        /// mismo %), 3 = solo público. Con varias filas gana el % MAYOR por audiencia (el mejor
        /// para el cliente, que es el que Nesto acabaría aplicando).
        /// </summary>
        internal static DescuentosWebCalculados CalcularDescuentosWeb(
            System.Collections.Generic.IEnumerable<DescuentosProducto> filas, decimal? pvp)
        {
            DescuentosWebCalculados resultado = new DescuentosWebCalculados();
            if (filas == null)
            {
                return resultado;
            }

            foreach (DescuentosProducto fila in filas)
            {
                decimal? pctBase = null;
                if (fila.Descuento > 0)
                {
                    pctBase = Math.Round(fila.Descuento * 100M, 2);
                }
                else if (fila.Precio > 0 && pvp > 0)
                {
                    decimal derivado = Math.Round((1M - (fila.Precio.Value / pvp.Value)) * 100M, 2);
                    if (derivado > 0)
                    {
                        pctBase = derivado; // un Precio fijo POR ENCIMA del PVP no es una oferta
                    }
                }

                if (!pctBase.HasValue)
                {
                    continue;
                }

                decimal pctPublico = fila.DescuentoPublico.HasValue
                    ? Math.Round(fila.DescuentoPublico.Value * 100M, 2)
                    : pctBase.Value;

                if (fila.AmbitoWeb == 1 || fila.AmbitoWeb == 2)
                {
                    resultado.Profesional = Math.Max(resultado.Profesional ?? 0M, pctBase.Value);
                }
                if (fila.AmbitoWeb == 2 || fila.AmbitoWeb == 3)
                {
                    resultado.Publico = Math.Max(resultado.Publico ?? 0M, pctPublico);
                }
            }

            return resultado;
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
    public class DescuentosWebCalculados
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