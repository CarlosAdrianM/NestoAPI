using System;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Issue #547, corte (c): el ÚNICO punto de NestoAPI que escribe precios medios. Para un producto, en una
    /// transacción: lee su historial (lectura confirmada), lo calcula con <see cref="CalculadoraPrecioMedio"/> (la
    /// misma de la sombra, paridad con el SP) y escribe exactamente lo que escribiría el SP del domingo:
    /// <c>Productos.PrecioMedio</c> (solo empresa E, nunca la espejo), <c>LinPedidoCmp.Coste</c> por línea y
    /// <c>LinPedidoVta.Coste</c> por tramos de fecha (estado ≥ 2) y el último tramo (estados -1 y 1). Solo lo que cambia.
    ///
    /// Kits sin compras, productos sin compras y excluidos por montajes: NO se tocan (decisión de Carlos, 28/09/26:
    /// replicar la realidad; el SP tampoco los recalcula).
    /// </summary>
    public class EscritorPreciosMedios
    {
        private readonly IRepositorioEscrituraPreciosMedios repositorio;

        public EscritorPreciosMedios(IRepositorioEscrituraPreciosMedios repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public ResultadoEscrituraPrecioMedio RecalcularProducto(string empresa, string empresaEspejo, string producto)
        {
            if (string.IsNullOrWhiteSpace(empresa))
            {
                throw new ArgumentNullException(nameof(empresa));
            }
            if (string.IsNullOrWhiteSpace(producto))
            {
                throw new ArgumentNullException(nameof(producto));
            }
            return repositorio.RecalcularYEscribir(empresa.Trim(), (empresaEspejo ?? empresa).Trim(), producto.Trim(), Planificar);
        }

        /// <summary>Lo que se va a escribir para unos datos leídos de la BD. Puro.</summary>
        public static PlanEscrituraPrecioMedio Planificar(DatosProductoPrecioMedio datos)
        {
            if (datos == null)
            {
                throw new ArgumentNullException(nameof(datos));
            }
            ResultadoPrecioMedio calculo = CalculadoraPrecioMedio.Calcular(ConstructorHistorialPrecioMedio.Construir(datos));
            return PlanificadorEscrituraPreciosMedios.Planificar(datos, calculo);
        }
    }
}
