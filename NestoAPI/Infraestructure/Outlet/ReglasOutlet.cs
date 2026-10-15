using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Outlet
{
    /// <summary>
    /// NestoAPI#643: la regla ÚNICA del Outlet de la tienda. Un producto está en el Outlet si y solo si
    /// está en estado 4 Y está en la tienda (ficha web en PrestashopProductos y la puerta de publicación lo
    /// da por publicable). Lo que no está en la tienda no entra nunca por este motivo: no se crean productos
    /// para meterlos en el Outlet (Carlos, 15/10/26: «seguro que hay un motivo por el que no está»).
    ///
    /// <para>El Outlet son tres categorías secundarias de Nesto (ProductosCategoriasSecundarias); cada
    /// producto va a UNA, según su grupo y subgrupo (sacado de cómo estaban puestos a mano los 651 del
    /// 15/10/26):</para>
    /// <list type="bullet">
    /// <item><b>PEL/OUT</b> «Outlet Peluquería»: el grupo PEL.</item>
    /// <item><b>COS/OUM</b> «Outlet Maquillaje y Uñas»: COS 201 (línea de color), COS 202 (esmaltado
    /// permanente), COS MAQ (maquillaje) y ACC 004/005/006 (accesorios de línea de color, manicura y
    /// pedicura).</item>
    /// <item><b>COS/OUT</b> «Outlet Estética»: todo lo demás (COS, ACC, APA, ACP…).</item>
    /// </list>
    ///
    /// <para>Clase pura, sin base de datos: el cálculo de qué entra, qué sale y qué cambia de grupo se
    /// prueba entero en ReglasOutletTests.</para>
    /// </summary>
    public static class ReglasOutlet
    {
        public const short ESTADO_OUTLET = 4;

        public static readonly CategoriaOutlet PELUQUERIA = new CategoriaOutlet("PEL", "OUT");
        public static readonly CategoriaOutlet MAQUILLAJE_Y_UNAS = new CategoriaOutlet("COS", "OUM");
        public static readonly CategoriaOutlet ESTETICA = new CategoriaOutlet("COS", "OUT");

        /// <summary>Las tres categorías Outlet (las únicas que este mantenimiento pone o quita).</summary>
        public static readonly IReadOnlyList<CategoriaOutlet> TODAS = new[] { PELUQUERIA, MAQUILLAJE_Y_UNAS, ESTETICA };

        private static readonly HashSet<string> SUBGRUPOS_COS_MAQUILLAJE_Y_UNAS =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "201", "202", "MAQ" };

        private static readonly HashSet<string> SUBGRUPOS_ACC_MAQUILLAJE_Y_UNAS =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "004", "005", "006" };

        /// <summary>La categoría Outlet que le toca a un producto de ese grupo y subgrupo (si está en el Outlet).</summary>
        public static CategoriaOutlet CategoriaQueToca(string grupo, string subgrupo)
        {
            string g = grupo?.Trim() ?? string.Empty;
            string s = subgrupo?.Trim() ?? string.Empty;
            if (string.Equals(g, "PEL", StringComparison.OrdinalIgnoreCase))
            {
                return PELUQUERIA;
            }
            if ((string.Equals(g, "COS", StringComparison.OrdinalIgnoreCase) && SUBGRUPOS_COS_MAQUILLAJE_Y_UNAS.Contains(s))
                || (string.Equals(g, "ACC", StringComparison.OrdinalIgnoreCase) && SUBGRUPOS_ACC_MAQUILLAJE_Y_UNAS.Contains(s)))
            {
                return MAQUILLAJE_Y_UNAS;
            }
            return ESTETICA;
        }

        /// <summary>¿Es (grupo, subgrupo) una de las tres categorías Outlet?</summary>
        public static bool EsCategoriaOutlet(string grupo, string subgrupo)
        {
            CategoriaOutlet c = new CategoriaOutlet(grupo, subgrupo);
            return TODAS.Contains(c);
        }

        /// <summary>¿Debe estar en el Outlet? Estado 4 y en la tienda; nada más.</summary>
        public static bool DebeEstarEnOutlet(SituacionOutletProducto producto)
        {
            return producto != null && producto.Estado == ESTADO_OUTLET && producto.EstaEnTienda;
        }

        /// <summary>
        /// Compara lo que hay con lo que debería haber y devuelve solo los productos que cambian:
        /// <list type="bullet">
        /// <item><b>Entra</b>: debe estar y no tiene ninguna categoría Outlet → se le pone la suya.</item>
        /// <item><b>Sale</b>: tiene alguna categoría Outlet y no debe estar → se le quitan todas.</item>
        /// <item><b>Cambia de grupo</b>: debe estar, pero no tiene exactamente la suya (tiene otra, o la suya y
        /// otra más) → se le quitan las que sobran y se le pone la suya si le falta.</item>
        /// </list>
        /// </summary>
        public static List<CambioOutlet> Calcular(IEnumerable<SituacionOutletProducto> productos)
        {
            List<CambioOutlet> cambios = new List<CambioOutlet>();
            foreach (SituacionOutletProducto p in productos ?? Enumerable.Empty<SituacionOutletProducto>())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Numero))
                {
                    continue;
                }
                List<CategoriaOutlet> actuales = (p.CategoriasOutletActuales ?? new List<CategoriaOutlet>())
                    .Where(c => c != null && EsCategoriaOutlet(c.Grupo, c.Subgrupo))
                    .Select(c => new CategoriaOutlet(c.Grupo, c.Subgrupo))
                    .Distinct()
                    .ToList();

                if (DebeEstarEnOutlet(p))
                {
                    CategoriaOutlet toca = CategoriaQueToca(p.Grupo, p.Subgrupo);
                    List<CategoriaOutlet> sobran = actuales.Where(c => !c.Equals(toca)).ToList();
                    bool falta = !actuales.Contains(toca);
                    if (!falta && sobran.Count == 0)
                    {
                        continue;
                    }
                    cambios.Add(new CambioOutlet(p, actuales.Count == 0 ? TiposCambioOutlet.ENTRA : TiposCambioOutlet.CAMBIA_DE_GRUPO,
                        poner: falta ? toca : null, quitar: sobran));
                }
                else if (actuales.Count > 0)
                {
                    cambios.Add(new CambioOutlet(p, TiposCambioOutlet.SALE, poner: null, quitar: actuales));
                }
            }
            return cambios
                .OrderBy(c => c.Tipo)
                .ThenBy(c => c.Numero, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Por qué sale (para la lista de la previsualización y el correo).</summary>
        internal static string MotivoSalida(SituacionOutletProducto p)
        {
            if (p.Estado != ESTADO_OUTLET)
            {
                return $"está en estado {p.Estado}, no en estado {ESTADO_OUTLET}";
            }
            if (!p.TieneFichaWeb)
            {
                return "está en estado 4 pero no tiene ficha web (PrestashopProductos): no está en la tienda";
            }
            return $"está en estado 4 pero la puerta de publicación no lo deja ir a la tienda: {p.MotivoNoPublicable}";
        }
    }

    /// <summary>Una categoría secundaria (grupo + subgrupo), recortada y en mayúsculas.</summary>
    public sealed class CategoriaOutlet : IEquatable<CategoriaOutlet>
    {
        public CategoriaOutlet(string grupo, string subgrupo)
        {
            Grupo = (grupo ?? string.Empty).Trim().ToUpperInvariant();
            Subgrupo = (subgrupo ?? string.Empty).Trim().ToUpperInvariant();
        }

        public string Grupo { get; }
        public string Subgrupo { get; }

        public bool Equals(CategoriaOutlet other) => other != null && Grupo == other.Grupo && Subgrupo == other.Subgrupo;
        public override bool Equals(object obj) => Equals(obj as CategoriaOutlet);
        public override int GetHashCode() => (Grupo + "/" + Subgrupo).GetHashCode();
        public override string ToString() => Grupo + "/" + Subgrupo;
    }

    /// <summary>Lo que se sabe de un producto para decidir si va al Outlet (ya cargado y recortado).</summary>
    public class SituacionOutletProducto
    {
        public string Numero { get; set; }
        public string Nombre { get; set; }
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        public short Estado { get; set; }

        /// <summary>Tiene fila en PrestashopProductos (ficha web).</summary>
        public bool TieneFichaWeb { get; set; }

        /// <summary>La puerta de publicación (PuertaPublicacionTienda) lo deja ir a la tienda.</summary>
        public bool Publicable { get; set; }

        /// <summary>Por qué la puerta no lo deja ir (null si es publicable o no se evaluó).</summary>
        public string MotivoNoPublicable { get; set; }

        /// <summary>«Está en la tienda» para #643: ficha web y la puerta lo da por publicable.</summary>
        public bool EstaEnTienda => TieneFichaWeb && Publicable;

        public List<CategoriaOutlet> CategoriasOutletActuales { get; set; } = new List<CategoriaOutlet>();
    }

    public static class TiposCambioOutlet
    {
        public const string ENTRA = "Entra";
        public const string SALE = "Sale";
        public const string CAMBIA_DE_GRUPO = "Cambia de grupo";
    }

    /// <summary>Un producto que cambia: qué categoría Outlet se le pone y cuáles se le quitan.</summary>
    public class CambioOutlet
    {
        public CambioOutlet()
        {
        }

        internal CambioOutlet(SituacionOutletProducto p, string tipo, CategoriaOutlet poner, List<CategoriaOutlet> quitar)
        {
            Numero = p.Numero?.Trim();
            Nombre = p.Nombre?.Trim();
            Grupo = p.Grupo?.Trim();
            Subgrupo = p.Subgrupo?.Trim();
            Estado = p.Estado;
            Tipo = tipo;
            Poner = poner?.ToString();
            Quitar = (quitar ?? new List<CategoriaOutlet>()).Select(c => c.ToString()).ToList();
            CategoriaPoner = poner;
            CategoriasQuitar = quitar ?? new List<CategoriaOutlet>();
            // Solo se republica lo que tiene ficha web: lo que no está en la tienda no se publica por este
            // motivo (#643, «nunca crear productos»). La puerta vuelve a decidir al publicar.
            Republicar = p.TieneFichaWeb;
            Motivo = tipo == TiposCambioOutlet.SALE
                ? ReglasOutlet.MotivoSalida(p)
                : tipo == TiposCambioOutlet.ENTRA
                    ? "está en estado 4 y en la tienda"
                    : $"le toca {poner?.ToString() ?? ReglasOutlet.CategoriaQueToca(p.Grupo, p.Subgrupo).ToString()} por su grupo {p.Grupo?.Trim()}/{p.Subgrupo?.Trim()}";
        }

        public string Numero { get; set; }
        public string Nombre { get; set; }
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        public short Estado { get; set; }

        /// <summary>Entra, Sale o Cambia de grupo (<see cref="TiposCambioOutlet"/>).</summary>
        public string Tipo { get; set; }

        /// <summary>La categoría Outlet que se le pone («COS/OUM»), o null.</summary>
        public string Poner { get; set; }

        /// <summary>Las categorías Outlet que se le quitan.</summary>
        public List<string> Quitar { get; set; } = new List<string>();

        /// <summary>Si se encola en Nesto_sync al aplicarlo (tiene ficha web).</summary>
        public bool Republicar { get; set; }

        public string Motivo { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        internal CategoriaOutlet CategoriaPoner { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        internal List<CategoriaOutlet> CategoriasQuitar { get; set; } = new List<CategoriaOutlet>();
    }
}
