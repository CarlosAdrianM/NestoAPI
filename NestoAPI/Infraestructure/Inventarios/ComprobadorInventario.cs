using NestoAPI.Models.Inventarios;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Inventarios
{
    /// <summary>NestoAPI#790: los códigos de las comprobaciones (Nesto los usa para saber qué es cada lista).</summary>
    public static class CodigosComprobacionInventario
    {
        // Bloqueantes
        public const string OTRA_FECHA = "OtraFecha";
        public const string CANTIDAD_DISPARATADA = "CantidadDisparatada";
        public const string FICTICIO = "Ficticio";
        public const string UBICACION_MAL_FORMADA = "UbicacionMalFormada";
        public const string SIN_UBICACION = "SinUbicacion";
        public const string UBICACION_INEXISTENTE = "UbicacionInexistente";
        /// <summary>NestoAPI#784: más de una fila pendiente del mismo producto (y ubicación) ese día: el SP apuntaría la diferencia dos veces.</summary>
        public const string FILAS_DUPLICADAS = "FilasDuplicadas";
        /// <summary>
        /// NestoAPI#784 (tiendas): producto ya contabilizado ese día que se ha vuelto a abrir («Contar otra vez») y sigue con contado 0
        /// teniendo stock: contabilizarlo lo deja a cero. Se puede confirmar (<c>SeConfirma</c>, ConfirmarContadoCero).
        /// </summary>
        public const string RECONTADA_SIN_CONTAR = "RecontadaSinContar";
        // Avisos
        public const string ANULADO_CON_STOCK = "AnuladoConStock";
        public const string SIN_PRECIO_MEDIO = "SinPrecioMedio";
        public const string CALCULADO_NEGATIVO = "CalculadoNegativo";
        public const string UBICACION_SIN_RELLENAR = "UbicacionSinRellenar";
        public const string GRUPOS_MEZCLADOS = "GruposMezclados";
        /// <summary>NestoAPI#784 (tiendas): filas con contado 0 de productos con stock: ¿de verdad no hay ninguno o no se ha contado?</summary>
        public const string CONTADO_CERO_CON_STOCK = "ContadoCeroConStock";
    }

    /// <summary>
    /// NestoAPI#790 (épica #784): las consultas con las que Carlos revisaba el inventario en SSMS antes de contabilizarlo, sobre las
    /// filas pendientes (Estado 1, sin NºTraspaso) del almacén. Pura: el SQL solo lee (<see cref="IRepositorioContabilizarInventario"/>).
    /// Bloqueantes (no se deja contabilizar): filas con otra fecha, cantidades disparatadas, ficticios con stock y, en almacenes con
    /// ubicaciones, ubicaciones mal escritas, sin ubicación o que no existen. Avisos: anulados con stock, sin precio medio, calculado
    /// negativo y, con ubicaciones, ubicaciones con stock sin inventariar y ubicaciones con grupos mezclados.
    /// NestoAPI#784: bloquean también las filas duplicadas (mismo producto y ubicación ese día) y, en las tiendas, lo ya contabilizado
    /// ese día que se ha vuelto a abrir y sigue con contado 0 (este se puede confirmar); y en las tiendas se avisa del contado 0 con stock.
    /// </summary>
    public static class ComprobadorInventario
    {
        /// <summary>Más que esto no se cuenta de un producto en un sitio: es un código leído en la cantidad.</summary>
        public const int STOCK_REAL_MAXIMO = 9999;
        public const int LONGITUD_UBICACION = 3;
        public const int ESTADO_PRODUCTO_ANULADO = -1;

        /// <param name="contabilizadas">NestoAPI#784: las filas ya contabilizadas ese día en el almacén (solo hacen falta en las tiendas).</param>
        public static ComprobacionesInventarioDTO Comprobar(string almacen, DateTime fecha, bool controlUbicaciones,
            IEnumerable<FilaInventarioPendiente> pendientes, IEnumerable<UbicacionAlmacenInventario> ubicaciones,
            IEnumerable<FilaInventarioContabilizada> contabilizadas = null)
        {
            DateTime dia = fecha.Date;
            List<FilaInventarioPendiente> todas = (pendientes ?? Enumerable.Empty<FilaInventarioPendiente>()).ToList();
            List<FilaInventarioPendiente> delDia = todas.Where(f => f.Fecha.Date == dia).ToList();
            var resultado = new ComprobacionesInventarioDTO
            {
                Almacen = almacen,
                Fecha = dia,
                ControlUbicaciones = controlUbicaciones
            };

            // ---- Bloqueantes ----
            Anadir(resultado.Bloqueantes, CodigosComprobacionInventario.OTRA_FECHA,
                "Filas pendientes de este almacén con otra fecha (el procedimiento contabiliza todas las del usuario: bórrelas o cámbieles la fecha)",
                todas.Where(f => f.Fecha.Date != dia));
            Anadir(resultado.Bloqueantes, CodigosComprobacionInventario.CANTIDAD_DISPARATADA,
                "Cantidades disparatadas: más de 9.999 o una referencia leída como cantidad",
                delDia.Where(EsCantidadDisparatada));
            Anadir(resultado.Bloqueantes, CodigosComprobacionInventario.FICTICIO,
                "Productos ficticios con stock contado",
                delDia.Where(f => f.Ficticio && (f.StockReal ?? 0) != 0));
            // NestoAPI#784 (09/11/26): el SP apunta la diferencia de cada fila; dos del mismo producto (y ubicación) la apuntan dos veces.
            Anadir(resultado.Bloqueantes, CodigosComprobacionInventario.FILAS_DUPLICADAS,
                "Más de una fila sin contabilizar del mismo producto (y ubicación) este día: deje solo una (se apuntaría la diferencia de cada una)",
                delDia.GroupBy(f => ClaveProducto(f.Producto) + "#" + Clave(f.Pasillo, f.Fila, f.Columna), StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .SelectMany(g => g));

            // NestoAPI#784 (tiendas): lo ya contabilizado ese día que se ha vuelto a abrir («Contar otra vez») lleva como teórico lo que
            // se contó; con contado 0 sin contar, contabilizarlo lo deja a cero (el 42341 de Paloma). Se confirma con ConfirmarContadoCero.
            var yaContabilizados = new HashSet<string>(
                (controlUbicaciones ? Enumerable.Empty<FilaInventarioContabilizada>() : contabilizadas ?? Enumerable.Empty<FilaInventarioContabilizada>())
                    .Where(c => c != null && c.Fecha.Date == dia)
                    .Select(c => ClaveProducto(c.Producto)),
                StringComparer.OrdinalIgnoreCase);
            List<FilaInventarioPendiente> recontadasSinContar = delDia
                .Where(f => (f.StockReal ?? 0) == 0 && (f.StockCalculado ?? 0) != 0 && yaContabilizados.Contains(ClaveProducto(f.Producto)))
                .ToList();
            Anadir(resultado.Bloqueantes, CodigosComprobacionInventario.RECONTADA_SIN_CONTAR,
                "Productos ya contados y contabilizados este día que se han vuelto a abrir y siguen con contado 0: contabilizarlos los deja a cero. " +
                "Cuéntelos o confirme que de verdad no hay ninguno",
                recontadasSinContar, seConfirma: true);

            if (controlUbicaciones)
            {
                List<UbicacionAlmacenInventario> huecos = (ubicaciones ?? Enumerable.Empty<UbicacionAlmacenInventario>()).ToList();
                var existentes = new HashSet<string>(huecos.Select(u => Clave(u.Pasillo, u.Fila, u.Columna)), StringComparer.OrdinalIgnoreCase);

                Anadir(resultado.Bloqueantes, CodigosComprobacionInventario.UBICACION_MAL_FORMADA,
                    "Ubicaciones mal escritas (pasillo, fila y columna llevan 3 caracteres)",
                    delDia.Where(f => f.Pasillo != null && !EstaBienFormada(f)));
                Anadir(resultado.Bloqueantes, CodigosComprobacionInventario.SIN_UBICACION,
                    "Productos que se ubican contados sin ubicación",
                    delDia.Where(f => f.Pasillo == null && f.Ubicar));
                Anadir(resultado.Bloqueantes, CodigosComprobacionInventario.UBICACION_INEXISTENTE,
                    "Ubicaciones que no existen en el almacén",
                    delDia.Where(f => f.Pasillo != null && EstaBienFormada(f) && !existentes.Contains(Clave(f.Pasillo, f.Fila, f.Columna))));
            }

            // ---- Avisos ----
            Anadir(resultado.Avisos, CodigosComprobacionInventario.ANULADO_CON_STOCK,
                "Productos anulados con stock contado",
                delDia.Where(f => f.EstadoProducto == ESTADO_PRODUCTO_ANULADO && (f.StockReal ?? 0) != 0));
            Anadir(resultado.Avisos, CodigosComprobacionInventario.SIN_PRECIO_MEDIO,
                "Productos sin precio medio con stock contado (se valorarán a cero)",
                delDia.Where(f => (f.PrecioMedio ?? 0) == 0 && (f.StockReal ?? 0) != 0));
            Anadir(resultado.Avisos, CodigosComprobacionInventario.CALCULADO_NEGATIVO,
                "Stock calculado negativo",
                delDia.Where(f => (f.StockCalculado ?? 0) < 0));
            if (!controlUbicaciones)
            {
                // NestoAPI#784: la trampa de abrir el evolutivo y no contar: contado 0 de un producto con stock (sin repetir las de arriba).
                var recontadas = new HashSet<int>(recontadasSinContar.Select(f => f.NumeroOrden));
                Anadir(resultado.Avisos, CodigosComprobacionInventario.CONTADO_CERO_CON_STOCK,
                    "Filas con contado 0 de productos que tienen stock: compruebe que de verdad no hay ninguno y no que se han quedado sin contar",
                    delDia.Where(f => (f.StockReal ?? 0) == 0 && (f.StockCalculado ?? 0) > 0 && !recontadas.Contains(f.NumeroOrden)));
            }

            if (controlUbicaciones)
            {
                List<UbicacionAlmacenInventario> huecos = (ubicaciones ?? Enumerable.Empty<UbicacionAlmacenInventario>()).ToList();
                var inventariadas = new HashSet<string>(delDia.Where(f => f.Pasillo != null).Select(f => Clave(f.Pasillo, f.Fila, f.Columna)),
                    StringComparer.OrdinalIgnoreCase);
                List<FilaComprobacionInventarioDTO> sinRellenar = huecos
                    .Where(u => u.Cantidad != 0 && !inventariadas.Contains(Clave(u.Pasillo, u.Fila, u.Columna)))
                    .OrderBy(u => u.Pasillo).ThenBy(u => u.Fila).ThenBy(u => u.Columna).ThenBy(u => u.Producto)
                    .Select(u => new FilaComprobacionInventarioDTO
                    {
                        Producto = u.Producto,
                        Descripcion = u.Descripcion,
                        StockCalculado = u.Cantidad,
                        Pasillo = u.Pasillo,
                        Fila = u.Fila,
                        Columna = u.Columna
                    })
                    .ToList();
                AnadirFilas(resultado.Avisos, CodigosComprobacionInventario.UBICACION_SIN_RELLENAR,
                    "Ubicaciones con stock que no se han inventariado (se quedan como están)", sinRellenar);

                Anadir(resultado.Avisos, CodigosComprobacionInventario.GRUPOS_MEZCLADOS,
                    "Ubicaciones con productos de grupos distintos",
                    delDia.Where(f => f.Pasillo != null)
                        .GroupBy(f => Clave(f.Pasillo, f.Fila, f.Columna), StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Select(f => f.Grupo?.Trim()).Where(gr => !string.IsNullOrEmpty(gr))
                            .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                        .SelectMany(g => g));
            }

            resultado.Recuento = delDia
                .GroupBy(f => f.Usuario?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g => new RecuentoUsuarioInventarioDTO { Usuario = g.First().Usuario?.Trim() ?? string.Empty, Filas = g.Count() })
                .OrderBy(r => r.Usuario, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return resultado;
        }

        /// <summary>
        /// Más de 9.999, o la referencia de un producto leída en la cantidad. Si coincide con el stock calculado no es un error de
        /// lectura (en ALG hay un producto con 17.686 unidades de verdad).
        /// </summary>
        internal static bool EsCantidadDisparatada(FilaInventarioPendiente fila)
        {
            int real = fila.StockReal ?? 0;
            if (real == (fila.StockCalculado ?? 0))
            {
                return false;
            }
            return real > STOCK_REAL_MAXIMO || fila.StockRealEsReferencia;
        }

        internal static bool EstaBienFormada(FilaInventarioPendiente fila) =>
            Longitud(fila.Pasillo) == LONGITUD_UBICACION && Longitud(fila.Fila) == LONGITUD_UBICACION && Longitud(fila.Columna) == LONGITUD_UBICACION;

        /// <summary>Como LEN() de SQL: sin los espacios del final.</summary>
        private static int Longitud(string valor) => valor?.TrimEnd().Length ?? 0;

        private static string Clave(string pasillo, string fila, string columna) =>
            (pasillo?.Trim() ?? string.Empty) + "|" + (fila?.Trim() ?? string.Empty) + "|" + (columna?.Trim() ?? string.Empty);

        private static string ClaveProducto(string producto) => producto?.Trim().ToUpperInvariant() ?? string.Empty;

        private static void Anadir(List<ComprobacionInventarioDTO> lista, string codigo, string titulo, IEnumerable<FilaInventarioPendiente> filas,
            bool seConfirma = false)
        {
            AnadirFilas(lista, codigo, titulo, filas
                .OrderBy(f => f.Pasillo).ThenBy(f => f.Fila).ThenBy(f => f.Columna).ThenBy(f => f.Producto).ThenBy(f => f.NumeroOrden)
                .Select(f => new FilaComprobacionInventarioDTO
                {
                    NºOrden = f.NumeroOrden,
                    Producto = f.Producto,
                    Descripcion = f.Descripcion,
                    StockCalculado = f.StockCalculado,
                    StockReal = f.StockReal,
                    Pasillo = f.Pasillo,
                    Fila = f.Fila,
                    Columna = f.Columna,
                    Usuario = f.Usuario,
                    Fecha = f.Fecha
                })
                .ToList(), seConfirma);
        }

        private static void AnadirFilas(List<ComprobacionInventarioDTO> lista, string codigo, string titulo, List<FilaComprobacionInventarioDTO> filas,
            bool seConfirma = false)
        {
            if (filas.Count == 0)
            {
                return;
            }
            lista.Add(new ComprobacionInventarioDTO { Codigo = codigo, Titulo = titulo, Filas = filas, SeConfirma = seConfirma });
        }
    }
}
