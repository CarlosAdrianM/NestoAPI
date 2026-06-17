namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Para un pedido concreto (destino/CP, peso, reembolso) elige la agencia+servicio MÁS
    /// BARATO, teniendo en cuenta el recargo de combustible de cada agencia. Sustituye al
    /// comparador de Nesto (AgenciasViewModel.TarifaMasEconomica), ahora server-side.
    /// </summary>
    public class ComparadorAgencias
    {
        // Canteras juega en otro nivel: solo Canarias y no depende del precio. Cualquier envío a
        // Canarias va siempre por Canteras, así que no se compara por tarifa (decisión Carlos).
        private const int AgenciaCanteras = 11;

        private readonly IRegistroTarifas _registro;
        private readonly IProveedorRecargoCombustible _recargoCombustible;

        public ComparadorAgencias(IRegistroTarifas registro, IProveedorRecargoCombustible recargoCombustible)
        {
            _registro = registro;
            _recargoCombustible = recargoCombustible;
        }

        public OpcionEnvioAgencia MasEconomica(string empresa, string codigoPostal, decimal peso, decimal reembolso)
        {
            ZonasEnvioAgencia zona = CalculadoraZonaEnvio.CalcularZona(codigoPostal);

            // Canarias siempre va por Canteras, sin comparar precio.
            if (zona == ZonasEnvioAgencia.CanariasMayores || zona == ZonasEnvioAgencia.CanariasMenores)
            {
                return new OpcionEnvioAgencia
                {
                    AgenciaId = AgenciaCanteras,
                    NombreServicio = "Canteras"
                };
            }

            OpcionEnvioAgencia mejor = null;

            foreach (ITarifaAgencia tarifa in _registro.Todas())
            {
                decimal fuel = _recargoCombustible.RecargoCombustible(empresa, tarifa.AgenciaId);
                decimal coste = CalculadoraCosteEnvio.CalcularCoste(tarifa, zona, peso, reembolso, fuel);

                // La tarifa no cubre la zona (coste centinela): no es una opción.
                if (coste == decimal.MaxValue)
                {
                    continue;
                }

                if (mejor == null || coste < mejor.Coste)
                {
                    mejor = new OpcionEnvioAgencia
                    {
                        AgenciaId = tarifa.AgenciaId,
                        ServicioId = tarifa.ServicioId,
                        NombreServicio = tarifa.NombreServicio,
                        Coste = coste
                    };
                }
            }

            return mejor;
        }
    }
}
