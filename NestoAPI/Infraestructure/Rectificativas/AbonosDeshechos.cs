using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rectificativas
{
    /// <summary>
    /// NestoAPI#907 (continuación de #892, OK de Carlos 24/11/26): deshacer un abono con pedido NUEVO en serie normal
    /// (<see cref="GestorCopiaPedidos.EsDeshacerAbono"/>, 11/11/26) no guarda vinculaciones (es una factura normal, no una
    /// rectificativa), así que la RV deshecha por ahí se podía volver a deshacer. Ahora deja un rastro propio en Modificaciones
    /// (Tabla <see cref="ReglaAbonoDeshecho.TABLA"/>, Anterior = «empresa|RV» buscable, Nuevo = este JSON) y
    /// <see cref="ReglasAbonoCopiaFactura.MotivoYaCorregida"/> lo mira junto a las vinculaciones de #892.
    /// </summary>
    public class RastroAbonoDeshecho
    {
        public string Empresa { get; set; }
        /// <summary>La rectificativa que se deshace («RV2600186»).</summary>
        public string Rectificativa { get; set; }
        /// <summary>El pedido nuevo (serie normal) que la deshace.</summary>
        public int Pedido { get; set; }
        public string Usuario { get; set; }
        public List<LineaAbonoDeshecho> Lineas { get; set; } = new List<LineaAbonoDeshecho>();
    }

    public class LineaAbonoDeshecho
    {
        /// <summary>Nº de orden de la línea NEGATIVA de la rectificativa.</summary>
        public int LineaOriginal { get; set; }
        /// <summary>Nº de orden de la línea positiva del pedido nuevo.</summary>
        public int LineaNueva { get; set; }
        public string Producto { get; set; }
        public decimal Cantidad { get; set; }

        /// <summary>Al leer: la línea del pedido nuevo sigue existiendo (si se borró el pedido o la línea, ya no deshace nada).</summary>
        [JsonIgnore]
        public bool Existe { get; set; }
        /// <summary>Al leer: la factura de la línea del pedido nuevo (null o vacío si aún no se ha facturado).</summary>
        [JsonIgnore]
        public string Factura { get; set; }
    }

    /// <summary>Una línea viva de un pedido (para saber si el rastro sigue valiendo y con qué factura).</summary>
    public class LineaPedidoAbonoDeshecho
    {
        public int Pedido { get; set; }
        public int Linea { get; set; }
        public string Factura { get; set; }
    }

    /// <summary>NestoAPI#907: reglas puras del rastro de un abono deshecho con pedido nuevo.</summary>
    internal static class ReglaAbonoDeshecho
    {
        internal const string TABLA = "AbonoDeshecho";

        /// <summary>La clave buscable (Modificaciones.Anterior): «1|RV2600186».</summary>
        internal static string Clave(string empresa, string rectificativa) => $"{empresa?.Trim()}|{rectificativa?.Trim()}";

        /// <summary>
        /// Un rastro por rectificativa de origen con las líneas negativas que se han deshecho (las copiadas en positivo). Vacío si
        /// no hay ninguna (no es deshacer un abono).
        /// </summary>
        internal static List<RastroAbonoDeshecho> Construir(string empresa, int pedido, IEnumerable<Models.Rectificativas.LineaCopiadaDTO> lineasCopiadas, string usuario)
        {
            return (lineasCopiadas ?? Enumerable.Empty<Models.Rectificativas.LineaCopiadaDTO>())
                .Where(l => l != null && l.CantidadOriginal < 0 && !string.IsNullOrWhiteSpace(l.FacturaOrigen))
                .GroupBy(l => l.FacturaOrigen.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new RastroAbonoDeshecho
                {
                    Empresa = empresa?.Trim(),
                    Rectificativa = g.Key,
                    Pedido = pedido,
                    Usuario = usuario,
                    Lineas = g.Select(l => new LineaAbonoDeshecho
                    {
                        LineaOriginal = l.LineaOrigen,
                        LineaNueva = l.NumeroLineaNueva,
                        Producto = l.Producto?.Trim(),
                        Cantidad = Math.Abs(l.CantidadOriginal)
                    }).ToList()
                })
                .ToList();
        }

        /// <summary>Marca qué líneas de cada rastro siguen en su pedido y con qué factura.</summary>
        internal static void CompletarConLineasVivas(IEnumerable<RastroAbonoDeshecho> rastros, IEnumerable<LineaPedidoAbonoDeshecho> lineasVivas)
        {
            Dictionary<(int, int), LineaPedidoAbonoDeshecho> vivas = (lineasVivas ?? Enumerable.Empty<LineaPedidoAbonoDeshecho>())
                .Where(l => l != null)
                .GroupBy(l => (l.Pedido, l.Linea))
                .ToDictionary(g => g.Key, g => g.First());
            foreach (RastroAbonoDeshecho rastro in (rastros ?? Enumerable.Empty<RastroAbonoDeshecho>()).Where(r => r?.Lineas != null))
            {
                foreach (LineaAbonoDeshecho linea in rastro.Lineas.Where(l => l != null))
                {
                    linea.Existe = vivas.TryGetValue((rastro.Pedido, linea.LineaNueva), out LineaPedidoAbonoDeshecho viva);
                    linea.Factura = viva?.Factura?.Trim();
                }
            }
        }

        /// <summary>
        /// Las correcciones que ya hizo un pedido nuevo (para <see cref="ReglasAbonoCopiaFactura.MotivoYaCorregida"/>): solo las
        /// líneas que siguen en su pedido; el documento es la factura de esa línea o, si aún no se ha facturado, el pedido.
        /// </summary>
        internal static List<ReglasAbonoCopiaFactura.AbonoAnterior> Correcciones(IEnumerable<RastroAbonoDeshecho> rastros)
        {
            var correcciones = new List<ReglasAbonoCopiaFactura.AbonoAnterior>();
            foreach (RastroAbonoDeshecho rastro in (rastros ?? Enumerable.Empty<RastroAbonoDeshecho>())
                .Where(r => r?.Lineas != null && !string.IsNullOrWhiteSpace(r.Rectificativa)))
            {
                foreach (LineaAbonoDeshecho linea in rastro.Lineas.Where(l => l != null && l.Existe))
                {
                    bool facturada = !string.IsNullOrWhiteSpace(linea.Factura);
                    correcciones.Add(new ReglasAbonoCopiaFactura.AbonoAnterior
                    {
                        FacturaOriginal = rastro.Rectificativa.Trim(),
                        LineaOriginal = linea.LineaOriginal,
                        Cantidad = linea.Cantidad > 0 ? linea.Cantidad : 1,
                        Documento = facturada ? linea.Factura.Trim() : $"el pedido {rastro.Pedido} (sin facturar)",
                        SinFacturar = !facturada
                    });
                }
            }
            return correcciones;
        }

        /// <summary>El rastro guardado (null si el JSON no se entiende).</summary>
        internal static RastroAbonoDeshecho Leer(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<RastroAbonoDeshecho>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>NestoAPI#907: dónde se guarda y se lee el rastro de los abonos deshechos con pedido nuevo.</summary>
    public interface IAlmacenAbonosDeshechos
    {
        Task Anotar(RastroAbonoDeshecho rastro);

        /// <summary>Los rastros de esas rectificativas, con cada línea marcada (<see cref="LineaAbonoDeshecho.Existe"/>, factura).</summary>
        Task<List<RastroAbonoDeshecho>> Leer(string empresa, IList<string> rectificativas);
    }

    /// <summary>NestoAPI#907: el rastro va en Modificaciones (sin tabla nueva ni script): Tabla «AbonoDeshecho», Anterior «empresa|RV».</summary>
    public class AlmacenAbonosDeshechos : IAlmacenAbonosDeshechos
    {
        private readonly NVEntities _db;

        public AlmacenAbonosDeshechos(NVEntities db)
        {
            _db = db;
        }

        public async Task Anotar(RastroAbonoDeshecho rastro)
        {
            if (rastro == null)
            {
                return;
            }
            _ = await _db.Database.ExecuteSqlCommandAsync(
                "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@p0, @p1, @p2, @p3)",
                ReglaAbonoDeshecho.TABLA, ReglaAbonoDeshecho.Clave(rastro.Empresa, rastro.Rectificativa),
                JsonConvert.SerializeObject(rastro), rastro.Usuario ?? string.Empty);
        }

        public async Task<List<RastroAbonoDeshecho>> Leer(string empresa, IList<string> rectificativas)
        {
            List<string> claves = (rectificativas ?? new List<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => ReglaAbonoDeshecho.Clave(empresa, r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (claves.Count == 0)
            {
                return new List<RastroAbonoDeshecho>();
            }
            var parametros = new List<object> { ReglaAbonoDeshecho.TABLA };
            parametros.AddRange(claves);
            string enLista = string.Join(", ", claves.Select((c, i) => $"@p{i + 1}"));
            List<string> filas = await _db.Database.SqlQuery<string>(
                $"SELECT Nuevo FROM dbo.Modificaciones WITH (NOLOCK) WHERE Tabla = @p0 AND Anterior IN ({enLista})",
                parametros.ToArray()).ToListAsync();
            List<RastroAbonoDeshecho> rastros = filas.Select(ReglaAbonoDeshecho.Leer).Where(r => r?.Lineas != null).ToList();
            if (!rastros.Any())
            {
                return rastros;
            }

            List<int> pedidos = rastros.Select(r => r.Pedido).Distinct().ToList();
            var parametrosLineas = new List<object> { empresa?.Trim() };
            parametrosLineas.AddRange(pedidos.Cast<object>());
            string enPedidos = string.Join(", ", pedidos.Select((p, i) => $"@p{i + 1}"));
            List<LineaPedidoAbonoDeshecho> vivas = await _db.Database.SqlQuery<LineaPedidoAbonoDeshecho>(
                "SELECT Número AS Pedido, [Nº Orden] AS Linea, RTRIM([Nº Factura]) AS Factura FROM LinPedidoVta WITH (NOLOCK) " +
                $"WHERE Empresa = @p0 AND Número IN ({enPedidos})",
                parametrosLineas.ToArray()).ToListAsync();
            ReglaAbonoDeshecho.CompletarConLineasVivas(rastros, vivas);
            return rastros;
        }
    }
}
