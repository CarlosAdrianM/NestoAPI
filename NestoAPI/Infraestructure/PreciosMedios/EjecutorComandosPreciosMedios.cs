using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Issue #547, corte (a) del incremental: ejecuta las sentencias de un <see cref="PlanEscrituraPrecioMedio"/> con un
    /// TOPE de filas por producto y pasada. Puro: la ejecución real de cada sentencia (una vuelta) se inyecta, así que se
    /// testa sin BD; lo usa <see cref="RepositorioEscrituraPreciosMediosSql"/> dentro de la transacción del producto.
    ///
    /// Por qué: un bestseller con un cambio en una compra antigua puede tener decenas de miles de líneas de venta que
    /// reescribir, y todas irían en UNA transacción (los bloqueos de fila de LinPedidoVta se mantienen hasta el commit).
    /// Con el tope, se escribe hasta él, se confirma, y el producto queda «pendiente» para la siguiente pasada. Como
    /// todos los UPDATE llevan el predicado «solo lo que cambia», la siguiente pasada recalcula el plan desde cero y
    /// continúa donde se quedó esta (lo ya escrito no vuelve a casar).
    ///
    /// El tope se comprueba ENTRE lotes y entre sentencias: se puede pasar como mucho en un lote
    /// (<see cref="SqlEscrituraPreciosMedios.TAMANO_LOTE_VENTAS"/>) o en una sentencia sin lotes (Productos y
    /// LinPedidoCmp, que son pocas filas).
    /// </summary>
    public static class EjecutorComandosPreciosMedios
    {
        /// <summary>
        /// Filas (Productos + LinPedidoCmp + LinPedidoVta) que el incremental escribe como mucho por producto en una
        /// pasada: 10 lotes de ventas. Cada lote se queda por debajo del escalado a bloqueo de tabla (5.000) y la
        /// transacción no retiene más de ~10.000 bloqueos de fila de golpe.
        /// </summary>
        public const int TOPE_FILAS_POR_PRODUCTO_Y_PASADA = 10 * SqlEscrituraPreciosMedios.TAMANO_LOTE_VENTAS;

        /// <summary>
        /// Ejecuta <paramref name="comandos"/> en orden sumando las filas en <paramref name="resultado"/>.
        /// <paramref name="ejecutarUnaVez"/> ejecuta UNA vuelta de una sentencia y devuelve las filas que ha cambiado
        /// ella (sin triggers). Si se llega al tope (o al tope de vueltas de la red) con algo por hacer, marca
        /// <see cref="ResultadoEscrituraPrecioMedio.QuedaPendiente"/> y deja de ejecutar.
        /// </summary>
        public static void Ejecutar(IEnumerable<ComandoEscrituraPrecioMedio> comandos, int topeFilas,
            Func<ComandoEscrituraPrecioMedio, int> ejecutarUnaVez, ResultadoEscrituraPrecioMedio resultado)
        {
            if (comandos == null)
            {
                throw new ArgumentNullException(nameof(comandos));
            }
            if (ejecutarUnaVez == null)
            {
                throw new ArgumentNullException(nameof(ejecutarUnaVez));
            }
            if (resultado == null)
            {
                throw new ArgumentNullException(nameof(resultado));
            }
            if (topeFilas <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(topeFilas), "El tope de filas tiene que ser positivo");
            }

            int total = 0;
            foreach (ComandoEscrituraPrecioMedio comando in comandos)
            {
                if (total >= topeFilas)
                {
                    resultado.QuedaPendiente = true; // quedan sentencias sin ejecutar
                    return;
                }
                int vueltas = 0;
                while (true)
                {
                    int filas = ejecutarUnaVez(comando);
                    Sumar(resultado, comando.Tabla, filas);
                    total += filas;
                    vueltas++;
                    if (!comando.EnLotes || filas < SqlEscrituraPreciosMedios.TAMANO_LOTE_VENTAS)
                    {
                        break; // sentencia terminada
                    }
                    if (total >= topeFilas || vueltas >= RepositorioEscrituraPreciosMediosSql.MAXIMO_VUELTAS_LOTE)
                    {
                        // El último lote ha salido lleno: puede quedar algo (si justo era lo último, la siguiente pasada
                        // no cambia nada y quita la marca).
                        resultado.QuedaPendiente = true;
                        return;
                    }
                }
            }
        }

        private static void Sumar(ResultadoEscrituraPrecioMedio resultado, TablaEscrituraPrecioMedio tabla, int filas)
        {
            switch (tabla)
            {
                case TablaEscrituraPrecioMedio.Productos: resultado.FilasProductos += filas; break;
                case TablaEscrituraPrecioMedio.LinPedidoCmp: resultado.FilasCompras += filas; break;
                case TablaEscrituraPrecioMedio.LinPedidoVta: resultado.FilasVentas += filas; break;
            }
        }
    }
}
