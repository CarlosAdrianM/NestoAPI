using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>Un parámetro de un <see cref="ComandoEscrituraPrecioMedio"/> (sin ADO, para poder testarlo).</summary>
    public sealed class ParametroEscrituraPrecioMedio
    {
        public ParametroEscrituraPrecioMedio(string nombre, SqlDbType tipo, object valor, int tamano = 0)
        {
            Nombre = nombre;
            Tipo = tipo;
            Valor = valor;
            Tamano = tamano;
        }

        public string Nombre { get; }
        public SqlDbType Tipo { get; }
        public object Valor { get; }
        public int Tamano { get; }
    }

    /// <summary>Qué tabla toca un comando (para contar filas en el resumen).</summary>
    public enum TablaEscrituraPrecioMedio
    {
        Productos,
        LinPedidoCmp,
        LinPedidoVta
    }

    /// <summary>
    /// Una sentencia de escritura del incremental. Si <see cref="EnLotes"/>, lleva <c>TOP (@lote)</c> y el
    /// repositorio la repite mientras cambie un lote entero (el predicado «solo lo que cambia» hace que las filas
    /// ya escritas no vuelvan a casar, así que el bucle termina).
    /// </summary>
    public sealed class ComandoEscrituraPrecioMedio
    {
        public TablaEscrituraPrecioMedio Tabla { get; set; }
        public string Texto { get; set; }
        public List<ParametroEscrituraPrecioMedio> Parametros { get; } = new List<ParametroEscrituraPrecioMedio>();
        public bool EnLotes { get; set; }
    }

    /// <summary>
    /// Genera el SQL (parametrizado) de las escrituras del incremental de precios medios (Issue #547, corte c).
    /// Puro: devuelve texto y parámetros; lo ejecuta <see cref="RepositorioEscrituraPreciosMediosSql"/>.
    ///
    /// Reglas (las mismas que el SP con el paso 1, Scripts/PreciosMedios_Paso1_SoloLoQueCambia.sql):
    /// - TODOS los UPDATE llevan el predicado «solo lo que cambia» <c>EXISTS (SELECT col EXCEPT SELECT nuevo)</c>:
    ///   una fila que ya tiene el valor no se toca (ni bloqueo, ni trgLinPedidoVtaUpd, ni trgProductosUpd).
    /// - Todos van acotados por producto (índice Producto+Estado+Empresa+Almacén en LinPedidoVta; Producto+Empresa
    ///   en LinPedidoCmp; PK en Productos).
    /// - LinPedidoVta en lotes de <see cref="TAMANO_LOTE_VENTAS"/> filas (<c>TOP (@lote)</c>): cada sentencia se queda
    ///   muy por debajo de las 5.000 filas a partir de las que SQL Server escala a bloqueo de TABLA.
    /// - Nada de índices filtrados ni columnas calculadas (#542): solo UPDATE planos.
    /// </summary>
    public static class SqlEscrituraPreciosMedios
    {
        /// <summary>Filas de LinPedidoVta por sentencia (lejos del umbral de escalado de bloqueos de 5.000).</summary>
        public const int TAMANO_LOTE_VENTAS = 1000;

        /// <summary>Rangos de fechas por sentencia de ventas (3 parámetros cada uno; el límite de SQL es 2.100).</summary>
        public const int RANGOS_POR_COMANDO = 300;

        /// <summary>Líneas de compra por sentencia (NºOrden IN (...)).</summary>
        public const int LINEAS_POR_COMANDO = 500;

        internal const string SQL_PRODUCTO = @"
UPDATE Productos SET PrecioMedio = @precioMedio
WHERE Empresa = @empresa AND Número = @producto
  AND EXISTS (SELECT PrecioMedio EXCEPT SELECT @precioMedio)";

        internal const string SQL_VENTAS_PENDIENTES = @"
UPDATE TOP (@lote) LinPedidoVta SET Coste = @coste
WHERE Producto = @producto AND Estado IN (-1, 1) AND Empresa IN (@empresa, @espejo)
  AND EXISTS (SELECT Coste EXCEPT SELECT @coste)";

        /// <summary>Todas las sentencias de un plan, en el orden en que se ejecutan (dentro de UNA transacción).</summary>
        public static List<ComandoEscrituraPrecioMedio> Generar(PlanEscrituraPrecioMedio plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            List<ComandoEscrituraPrecioMedio> comandos = new List<ComandoEscrituraPrecioMedio>();
            if (!plan.Procesado)
            {
                return comandos;
            }
            string espejo = string.IsNullOrWhiteSpace(plan.EmpresaEspejo) ? plan.Empresa : plan.EmpresaEspejo;

            if (plan.PrecioMedioNuevo.HasValue)
            {
                ComandoEscrituraPrecioMedio c = new ComandoEscrituraPrecioMedio { Tabla = TablaEscrituraPrecioMedio.Productos, Texto = SQL_PRODUCTO };
                c.Parametros.Add(Dinero("@precioMedio", plan.PrecioMedioNuevo.Value));
                c.Parametros.Add(Texto("@empresa", plan.Empresa));
                c.Parametros.Add(Texto("@producto", plan.Producto));
                comandos.Add(c);
            }

            comandos.AddRange(ComandosCompras(plan, espejo));
            comandos.AddRange(ComandosVentasPorFecha(plan, espejo));

            if (plan.CosteVentasPendientes.HasValue)
            {
                ComandoEscrituraPrecioMedio c = new ComandoEscrituraPrecioMedio
                {
                    Tabla = TablaEscrituraPrecioMedio.LinPedidoVta,
                    Texto = SQL_VENTAS_PENDIENTES,
                    EnLotes = true
                };
                c.Parametros.Add(Lote());
                c.Parametros.Add(Dinero("@coste", plan.CosteVentasPendientes.Value));
                c.Parametros.Add(Texto("@producto", plan.Producto));
                c.Parametros.Add(Texto("@empresa", plan.Empresa));
                c.Parametros.Add(Texto("@espejo", espejo));
                comandos.Add(c);
            }
            return comandos;
        }

        /// <summary>
        /// LinPedidoCmp: una sentencia por coste (y por trozos de <see cref="LINEAS_POR_COMANDO"/> líneas). Las líneas
        /// de una misma factura y fecha comparten coste, así que normalmente son pocas.
        /// </summary>
        private static IEnumerable<ComandoEscrituraPrecioMedio> ComandosCompras(PlanEscrituraPrecioMedio plan, string espejo)
        {
            foreach (IGrouping<decimal, int> grupo in plan.CostesCompra.GroupBy(c => c.Value, c => c.Key).OrderBy(g => g.Min()))
            {
                List<int> ordenes = grupo.OrderBy(o => o).ToList();
                for (int inicio = 0; inicio < ordenes.Count; inicio += LINEAS_POR_COMANDO)
                {
                    List<int> trozo = ordenes.Skip(inicio).Take(LINEAS_POR_COMANDO).ToList();
                    ComandoEscrituraPrecioMedio c = new ComandoEscrituraPrecioMedio { Tabla = TablaEscrituraPrecioMedio.LinPedidoCmp };
                    c.Parametros.Add(Dinero("@coste", grupo.Key));
                    c.Parametros.Add(Texto("@producto", plan.Producto));
                    c.Parametros.Add(Texto("@empresa", plan.Empresa));
                    c.Parametros.Add(Texto("@espejo", espejo));
                    List<string> nombres = new List<string>();
                    for (int i = 0; i < trozo.Count; i++)
                    {
                        string nombre = "@o" + i.ToString(CultureInfo.InvariantCulture);
                        nombres.Add(nombre);
                        c.Parametros.Add(new ParametroEscrituraPrecioMedio(nombre, SqlDbType.Int, trozo[i]));
                    }
                    c.Texto = @"
UPDATE LinPedidoCmp SET Coste = @coste
WHERE Producto = @producto AND Empresa IN (@empresa, @espejo) AND NºOrden IN (" + string.Join(", ", nombres) + @")
  AND EXISTS (SELECT Coste EXCEPT SELECT @coste)";
                    yield return c;
                }
            }
        }

        /// <summary>
        /// LinPedidoVta con estado ≥ 2: un UPDATE con los rangos en una tabla de valores (sin tabla temporal ni
        /// ##global), en lotes. Réplica del «update linpedidovta ... inner join ##global» del SP con el paso 1.
        /// </summary>
        private static IEnumerable<ComandoEscrituraPrecioMedio> ComandosVentasPorFecha(PlanEscrituraPrecioMedio plan, string espejo)
        {
            for (int inicio = 0; inicio < plan.RangosVenta.Count; inicio += RANGOS_POR_COMANDO)
            {
                List<RangoCosteVenta> trozo = plan.RangosVenta.Skip(inicio).Take(RANGOS_POR_COMANDO).ToList();
                ComandoEscrituraPrecioMedio c = new ComandoEscrituraPrecioMedio { Tabla = TablaEscrituraPrecioMedio.LinPedidoVta, EnLotes = true };
                c.Parametros.Add(Lote());
                c.Parametros.Add(Texto("@producto", plan.Producto));
                c.Parametros.Add(Texto("@empresa", plan.Empresa));
                c.Parametros.Add(Texto("@espejo", espejo));
                List<string> filas = new List<string>();
                for (int i = 0; i < trozo.Count; i++)
                {
                    string n = i.ToString(CultureInfo.InvariantCulture);
                    filas.Add("(@d" + n + ", @h" + n + ", @c" + n + ")");
                    c.Parametros.Add(new ParametroEscrituraPrecioMedio("@d" + n, SqlDbType.DateTime, trozo[i].Desde));
                    c.Parametros.Add(new ParametroEscrituraPrecioMedio("@h" + n, SqlDbType.DateTime, trozo[i].Hasta));
                    c.Parametros.Add(Dinero("@c" + n, trozo[i].Coste));
                }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine("UPDATE TOP (@lote) v SET Coste = t.Coste");
                sb.AppendLine("FROM LinPedidoVta v");
                sb.AppendLine("JOIN (VALUES " + string.Join(", ", filas) + ") AS t (Desde, Hasta, Coste)");
                sb.AppendLine("  ON v.[Fecha Albarán] >= t.Desde AND v.[Fecha Albarán] < t.Hasta");
                sb.AppendLine("WHERE v.Producto = @producto AND v.Estado >= 2 AND v.Empresa IN (@empresa, @espejo)");
                sb.Append("  AND EXISTS (SELECT v.Coste EXCEPT SELECT t.Coste)");
                c.Texto = sb.ToString();
                yield return c;
            }
        }

        private static ParametroEscrituraPrecioMedio Lote()
        {
            return new ParametroEscrituraPrecioMedio("@lote", SqlDbType.Int, TAMANO_LOTE_VENTAS);
        }

        private static ParametroEscrituraPrecioMedio Dinero(string nombre, decimal valor)
        {
            return new ParametroEscrituraPrecioMedio(nombre, SqlDbType.Money, valor);
        }

        /// <summary>VarChar como en la sombra: las columnas son char y un NVarChar podría perder la búsqueda por índice.</summary>
        private static ParametroEscrituraPrecioMedio Texto(string nombre, string valor)
        {
            return new ParametroEscrituraPrecioMedio(nombre, SqlDbType.VarChar, (object)valor?.Trim() ?? DBNull.Value, 50);
        }
    }
}
