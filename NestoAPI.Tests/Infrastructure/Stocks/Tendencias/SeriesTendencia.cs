using NestoAPI.Infraestructure.Stocks.Tendencias;
using System;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Stocks.Tendencias
{
    /// <summary>NestoAPI#630: series pequeñas para los tests. Mes de referencia: septiembre de 2026 (posición 59).</summary>
    internal static class SeriesTendencia
    {
        public static readonly DateTime MesReferencia = new DateTime(2026, 9, 1);
        public const int N = DatosProductoTendencia.MESES_SERIE;
        public const int FIN = DatosProductoTendencia.ULTIMO;

        public static int[] Constante(int valor) => Enumerable.Repeat(valor, N).ToArray();

        /// <summary>Serie con los últimos valores dados (el último es el mes de referencia) y ceros delante.</summary>
        public static int[] Ultimos(params int[] valores)
        {
            int[] serie = new int[N];
            Array.Copy(valores, 0, serie, N - valores.Length, valores.Length);
            return serie;
        }

        /// <summary>Un maduro de toda la vida: vende <paramref name="ventas"/> al mes desde hace 5 años y tiene <paramref name="stock"/>.</summary>
        public static DatosProductoTendencia Producto(string numero = "17404", int[] ventas = null, int stock = 90, int diasEnServir = 10,
            int multiplos = 1, decimal precio = 2m, short estado = 0)
        {
            return new DatosProductoTendencia
            {
                Producto = numero,
                Nombre = "Producto " + numero,
                Familia = "Genéricos",
                Grupo = "ACC",
                Subgrupo = "002",
                Estado = estado,
                PrecioMedio = precio,
                Proveedor = "40",
                DiasEnServir = diasEnServir,
                StockMaximoAlg = 555,
                StockMinimoAlg = 295,
                MultiplosAlg = multiplos,
                Ventas = ventas ?? Constante(30),
                StockFinMes = Constante(stock),
                FechaPrimeraVenta = new DateTime(2015, 1, 10),
                FechaPrimeraEntrada = new DateTime(2015, 1, 2)
            };
        }

        /// <summary>NestoAPI#655: un producto de fabricación propia (familia PRP, como Eva Visnú) con lote mínimo <paramref name="lote"/>.</summary>
        public static DatosProductoTendencia Propio(string numero = "20258", int[] ventas = null, int stock = 150, int diasEnServir = 60,
            int lote = 100, decimal precio = 2m, short estado = 0, string grupo = "COS")
        {
            DatosProductoTendencia d = Producto(numero, ventas, stock, diasEnServir, lote, precio, estado);
            d.Familia = "Eva Visnu";
            d.TipoExclusiva = "PRP";
            d.Grupo = grupo; // NestoAPI#655: la vida útil va por grupo (COS 12 meses)
            return d;
        }
    }
}
