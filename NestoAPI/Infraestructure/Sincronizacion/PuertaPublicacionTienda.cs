using NestoAPI.Infraestructure.Kits;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#432: la puerta de publicación de productos hacia la tienda online.
    ///
    /// <para>Decide, EN EL MOMENTO DE PUBLICAR y con los datos de ese momento, si un producto debe
    /// viajar a la tienda. No es una lista negra que se decide una vez: cada vez que algo encola el
    /// producto (el trigger de la ficha, el job de stocks, un encolado a mano) se vuelve a evaluar,
    /// así que un producto excluido hoy entra solo el día que deje de cumplir el motivo de
    /// exclusión, sin que nadie haga nada.</para>
    ///
    /// <para>Las reglas son la transcripción de la consulta legacy
    /// (Scripts/Legacy_ConsultaPrestashopConClasificacion.sql), que durante años decidió el
    /// catálogo en su WHERE y dejó de ejecutarse a finales de agosto de 2026. Son listas y campañas
    /// hard-codeadas A PROPÓSITO: es conocimiento de negocio que no existe en ningún otro sitio, y
    /// tenerlo aquí, con nombre, comentario y test, es la alternativa honesta a tenerlo perdido en
    /// una consulta de 3.780 líneas. Decisiones del 01/09/26 (Carlos):</para>
    ///
    /// <list type="bullet">
    /// <item>La puerta NO saca nada de la tienda: lo ya publicado se queda (la tienda recibe los
    /// productos inactivos y los activa a mano, así que hay una puerta humana al otro lado).</item>
    /// <item>La regla de stock del legacy ("sin stock fuera") NO se replica aquí: dejar de publicar
    /// un producto sin stock le congelaría el precio en la tienda. Eso lo resuelve la tienda
    /// ocultándolos, que ya recibe el stock por el pipeline.</item>
    /// <item>Los recambios de aparatología (APA/010), que el legacy excluía en bloque, entran si se
    /// han movido en el extracto en los últimos 3 años (decidido con los compañeros el 31/08/26).</item>
    /// <item>Las exclusiones que el legacy ya tenía comentadas (material promocional 999 de
    /// Anubis/Paraíso/Faby/Lisap, Essie, smellsphere, Alissi Brontë) NO se resucitan.</item>
    /// </list>
    /// </summary>
    public static class PuertaPublicacionTienda
    {
        /// <summary>
        /// Referencias que no van a la tienda, cada una con su porqué (los comentarios del legacy).
        /// </summary>
        private static readonly Dictionary<string, string> ReferenciasVetadas = new Dictionary<string, string>
        {
            { "36486", "cortapuntas: vetado en la consulta legacy" },
            { "37150", "referencia Starsoft" },
            { "37151", "referencia Starsoft" },
            { "37152", "referencia Starsoft" },
            { "37153", "referencia Starsoft" },
            { "37154", "referencia Starsoft" },
            { "32755", "Química Alemana (Laura, 24/01/22)" },
            { "40789", "Química Alemana (Laura, 24/01/22)" },
            { "22072", "cartílago de tiburón: Google no admite nada de especies protegidas" },
            { "24211", "cartílago de tiburón: Google no admite nada de especies protegidas" }
        };

        /// <summary>Referencias que entran aunque su estado no esté en la matriz (Carlos, 24/02/16).</summary>
        private static readonly HashSet<string> ReferenciasQueEntranSiempre = new HashSet<string> { "32819", "32845" };

        /// <summary>Familias (código) cuyo estado 3 entra en la tienda (Carlos, 01/03/17).</summary>
        private static readonly HashSet<string> FamiliasConEstadoTres = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Essie", "Eurostil", "Fama", "Klimt", "Lisap", "Thuya", "Sabrina", "agv", "anubis"
        };

        /// <summary>
        /// Familias (descripción, el "fabricante" del legacy) que no van a la tienda cuando el
        /// subgrupo es Otros aparatos.
        /// </summary>
        private static readonly HashSet<string> FabricantesVetadosEnOtrosAparatos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Maystar", "Depil ok", "Eva Visnú"
        };

        /// <summary>
        /// Evalúa la puerta y, si el producto es publicable, construye el DTO por el constructor
        /// único (#422). Este es el único camino por el que debe salir un producto hacia la tienda.
        /// </summary>
        public static async Task<ResultadoPuertaPublicacion> ConstruirParaPublicarSiPasa(
            Producto producto, NVEntities db, IProductoService productoService)
        {
            // La comprobación técnica de siempre: sin PVP o sin Estado (referencia reservada) no se
            // puede ni armar el mensaje.
            if (!SincronizacionJobsService.TieneDatosMinimosParaSincronizar(producto))
            {
                return ResultadoPuertaPublicacion.NoPublicable("sin PVP o sin Estado (referencia reservada)");
            }

            DatosPuertaPublicacion datos = await CargarDatos(producto, db).ConfigureAwait(false);
            ResultadoPuertaPublicacion resultado = Evaluar(datos);
            if (!resultado.Publicable)
            {
                return resultado;
            }

            resultado.Dto = await ProductoDTO.ConstruirParaPublicar(producto, db, productoService).ConfigureAwait(false);
            return resultado;
        }

        /// <summary>
        /// El núcleo puro de la puerta: todas las reglas, sin base de datos, para poder testearlas
        /// una a una.
        /// </summary>
        internal static ResultadoPuertaPublicacion Evaluar(DatosPuertaPublicacion datos)
        {
            if (datos.Ficticio)
            {
                return ResultadoPuertaPublicacion.NoPublicable("es un producto ficticio");
            }

            if (ReferenciasVetadas.TryGetValue(datos.Numero, out string motivoVeto))
            {
                return ResultadoPuertaPublicacion.NoPublicable(motivoVeto);
            }

            if (string.Equals(datos.Grupo, "MTP", StringComparison.OrdinalIgnoreCase))
            {
                return ResultadoPuertaPublicacion.NoPublicable("el grupo MTP no va a la tienda (07/06/21)");
            }

            if (EsRecambioDeAparatologia(datos) && !datos.TieneMovimientoExtractoTresAnnos)
            {
                return ResultadoPuertaPublicacion.NoPublicable(
                    "recambio de aparatología (APA/010) sin movimiento en el extracto en 3 años");
            }

            if (FabricantesVetadosEnOtrosAparatos.Contains(datos.DescripcionFamilia)
                && string.Equals(datos.DescripcionSubgrupo, "Otros aparatos", StringComparison.OrdinalIgnoreCase))
            {
                return ResultadoPuertaPublicacion.NoPublicable(
                    $"{datos.DescripcionFamilia} no va a la tienda en Otros aparatos");
            }

            // Un producto de baja SÍ se publica: es el mensaje con el que la tienda lo desactiva
            // (prestashop-nestosync#8). El legacy hacía lo equivalente exportando Activo = 0.
            if (datos.Estado < 0)
            {
                return ResultadoPuertaPublicacion.SiPublicable();
            }

            if (!datos.TieneProveedorPrincipalValido)
            {
                return ResultadoPuertaPublicacion.NoPublicable(
                    "tiene proveedores pero ninguno con orden 1 (el campo orden está mal; legacy 08/03/19)");
            }

            if (!EstadoPermitido(datos))
            {
                return ResultadoPuertaPublicacion.NoPublicable(
                    $"el estado {datos.Estado} de la familia {datos.FamiliaCodigo} no está en la matriz de estados publicables");
            }

            return ResultadoPuertaPublicacion.SiPublicable();
        }

        /// <summary>
        /// La matriz de estados del legacy (Carlos, 01/04/15 y sucesivas). Dos cláusulas del
        /// original no se transcriben porque eran muertas: APA subgrupos 001-009 con estado 0/1 y
        /// Silverfox con estado 1 (ambos ya entran por el estado in (0,1,4) general).
        /// </summary>
        private static bool EstadoPermitido(DatosPuertaPublicacion datos)
        {
            return datos.Estado == 0 || datos.Estado == 1 || datos.Estado == 4
                || (datos.Estado == 7 && string.Equals(datos.FamiliaCodigo, "LOréal", StringComparison.OrdinalIgnoreCase))
                || string.Equals(datos.FamiliaCodigo, "Schwarzkop", StringComparison.OrdinalIgnoreCase)
                || string.Equals(datos.FamiliaCodigo, "Eurostill", StringComparison.OrdinalIgnoreCase)
                || (datos.Estado == 3 && FamiliasConEstadoTres.Contains(datos.FamiliaCodigo))
                || ReferenciasQueEntranSiempre.Contains(datos.Numero)
                || datos.EsParteDeKitActivo;
        }

        private static bool EsRecambioDeAparatologia(DatosPuertaPublicacion datos)
        {
            return string.Equals(datos.Grupo, "APA", StringComparison.OrdinalIgnoreCase)
                && string.Equals(datos.Subgrupo, "010", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Todo lo que la puerta necesita saber del producto, cargado de una vez. Las consultas
        /// caras (extracto) solo se hacen cuando la regla que las usa aplica.
        /// </summary>
        internal static async Task<DatosPuertaPublicacion> CargarDatos(Producto producto, NVEntities db)
        {
            DatosPuertaPublicacion datos = new DatosPuertaPublicacion
            {
                Numero = producto.Número?.Trim(),
                Grupo = producto.Grupo?.Trim(),
                Subgrupo = producto.SubGrupo?.Trim(),
                FamiliaCodigo = producto.Familia?.Trim(),
                DescripcionFamilia = producto.Familia1?.Descripción?.Trim(),
                DescripcionSubgrupo = producto.SubGruposProducto?.Descripción?.Trim(),
                Ficticio = producto.Ficticio,
                Estado = producto.Estado ?? 0
            };

            // Regla del legacy (08/03/19): si el producto tiene proveedores, alguno debe ser el
            // principal (orden 1). Sin proveedores también entra (el LEFT JOIN del original).
            List<short> ordenes = await db.ProveedoresProductoes
                .Where(r => r.Empresa == producto.Empresa && r.Nº_Producto == producto.Número)
                .Select(r => r.Orden)
                .ToListAsync()
                .ConfigureAwait(false);
            datos.TieneProveedorPrincipalValido = !ordenes.Any() || ordenes.Contains((short)1);

            // La cláusula VIVA de los kits del legacy: un ASOCIADO de un kit cuyo titular está en
            // estado 0 entra aunque su propio estado no pase la matriz. (La otra mitad del union
            // del original —el titular— exigía estado 0, que ya pasa la matriz por sí solo.)
            datos.EsParteDeKitActivo = await db.Kits
                .AnyAsync(k => k.Empresa == producto.Empresa
                    && k.NúmeroAsociado == producto.Número
                    && db.Productos.Any(t => t.Empresa == k.Empresa && t.Número == k.Número && t.Estado == 0))
                .ConfigureAwait(false);

            if (EsRecambioDeAparatologia(datos))
            {
                DateTime haceTresAnnos = DateTime.Today.AddYears(-3);
                datos.TieneMovimientoExtractoTresAnnos = await db.ExtractosProducto
                    .AnyAsync(e => e.Número == producto.Número
                        && (e.Empresa == "1" || e.Empresa == "3")
                        && e.Fecha >= haceTresAnnos)
                    .ConfigureAwait(false);
            }

            return datos;
        }
    }

    /// <summary>Lo que la puerta contesta: pasa o no pasa, y si no pasa, por qué.</summary>
    public class ResultadoPuertaPublicacion
    {
        public bool Publicable { get; private set; }

        /// <summary>El porqué cuando no es publicable; null cuando sí lo es.</summary>
        public string Motivo { get; private set; }

        /// <summary>El DTO listo para publicar; solo cuando es publicable y se pidió construirlo.</summary>
        public ProductoDTO Dto { get; set; }

        public static ResultadoPuertaPublicacion SiPublicable()
        {
            return new ResultadoPuertaPublicacion { Publicable = true };
        }

        public static ResultadoPuertaPublicacion NoPublicable(string motivo)
        {
            return new ResultadoPuertaPublicacion { Publicable = false, Motivo = motivo };
        }
    }

    /// <summary>Los hechos del producto que la puerta evalúa, ya cargados y recortados.</summary>
    internal class DatosPuertaPublicacion
    {
        public string Numero { get; set; }
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        public string FamiliaCodigo { get; set; }
        public string DescripcionFamilia { get; set; }
        public string DescripcionSubgrupo { get; set; }
        public bool Ficticio { get; set; }
        public short Estado { get; set; }
        public bool TieneProveedorPrincipalValido { get; set; }
        public bool EsParteDeKitActivo { get; set; }
        public bool TieneMovimientoExtractoTresAnnos { get; set; }
    }
}
