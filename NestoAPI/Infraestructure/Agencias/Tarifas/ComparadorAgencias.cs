using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Para un pedido concreto (destino/CP, peso, reembolso) elige la agencia+servicio MÁS
    /// BARATO, teniendo en cuenta el recargo de combustible de cada agencia. Sustituye al
    /// comparador de Nesto (AgenciasViewModel.TarifaMasEconomica), ahora server-side.
    ///
    /// Soporta AGENCIAS SOMBRA: las agencias cuyos Id están en el conjunto sombra compiten en el
    /// <see cref="Ranking"/> (para medir cuántos envíos ganarían) pero <see cref="MasEconomica"/>
    /// nunca las devuelve (no se auto-seleccionan).
    /// </summary>
    public class ComparadorAgencias
    {
        // Canteras juega en otro nivel: solo Canarias y no depende del precio. Cualquier envío a
        // Canarias va siempre por Canteras, así que no se compara por tarifa (decisión Carlos).
        private const int AgenciaCanteras = 11;

        private readonly IRegistroTarifas _registro;
        private readonly IProveedorRecargoCombustible _recargoCombustible;
        private readonly HashSet<int> _agenciasSombra;

        public ComparadorAgencias(IRegistroTarifas registro, IProveedorRecargoCombustible recargoCombustible,
            IEnumerable<int> agenciasSombra = null)
        {
            _registro = registro;
            _recargoCombustible = recargoCombustible;
            _agenciasSombra = new HashSet<int>(agenciasSombra ?? Enumerable.Empty<int>());
        }

        /// <summary>
        /// La opción más barata que SÍ se puede seleccionar (excluye las agencias sombra). Null si
        /// ninguna agencia seleccionable cubre el destino.
        /// </summary>
        public OpcionEnvioAgencia MasEconomica(string empresa, string codigoPostal, decimal peso, decimal reembolso, string paisIso = "ES")
        {
            return Ranking(empresa, codigoPostal, peso, reembolso, paisIso)
                .FirstOrDefault(o => !_agenciasSombra.Contains(o.AgenciaId));
        }

        /// <summary>
        /// Todas las opciones (agencia + servicio) que cubren el destino, ordenadas de más barata a
        /// más cara, INCLUIDAS las sombra. Para Canarias devuelve solo Canteras (caso fijo). Sirve
        /// para detectar cuándo una sombra habría ganado y compararla con la agencia realmente usada.
        /// </summary>
        /// <summary>
        /// Coste de UNA agencia concreta (y, opcionalmente, de un servicio concreto) para el destino,
        /// con su recargo de combustible. A diferencia de <see cref="MasEconomica"/> NO elige la más
        /// barata: devuelve el coste de la agencia indicada (la realmente usada en el envío), para
        /// rellenar <c>EnviosAgencia.ImporteGasto</c> (NestoAPI#238). Incluye agencias sombra. Si no se
        /// indica servicio, devuelve el más barato de esa agencia. Null si esa agencia/servicio no
        /// cubre el destino o no tiene tarifa portada.
        /// </summary>
        public OpcionEnvioAgencia CosteDeAgencia(string empresa, string codigoPostal, decimal peso,
            decimal reembolso, int agenciaId, byte? servicioId = null, string paisIso = "ES")
        {
            // Ranking ya viene ordenado de más barato a más caro: el primero que case es el correcto.
            return Ranking(empresa, codigoPostal, peso, reembolso, paisIso)
                .FirstOrDefault(o => o.AgenciaId == agenciaId
                    && (servicioId == null || o.ServicioId == servicioId.Value));
        }

        public IReadOnlyList<OpcionEnvioAgencia> Ranking(string empresa, string codigoPostal, decimal peso, decimal reembolso, string paisIso = "ES")
        {
            // Canarias (siempre España) va siempre por Canteras, sin comparar precio.
            if (EsEspana(paisIso))
            {
                ZonasEnvioAgencia zonaEspana = CalculadoraZonaEnvio.CalcularZona(codigoPostal);
                if (zonaEspana == ZonasEnvioAgencia.CanariasMayores || zonaEspana == ZonasEnvioAgencia.CanariasMenores)
                {
                    return new List<OpcionEnvioAgencia>
                    {
                        new OpcionEnvioAgencia { AgenciaId = AgenciaCanteras, NombreServicio = "Canteras" }
                    };
                }
            }

            var opciones = new List<OpcionEnvioAgencia>();

            foreach (ITarifaAgencia tarifa in _registro.Todas())
            {
                decimal fuel = _recargoCombustible.RecargoCombustible(empresa, tarifa.AgenciaId);
                // Cada tarifa resuelve su zona PUERTAS ADENTRO a partir del destino canónico (CP + país).
                decimal coste = tarifa.CalcularCoste(codigoPostal, paisIso, peso, reembolso, fuel);

                // La tarifa no cubre el destino (coste centinela): no es una opción.
                if (coste == decimal.MaxValue)
                {
                    continue;
                }

                opciones.Add(new OpcionEnvioAgencia
                {
                    AgenciaId = tarifa.AgenciaId,
                    ServicioId = tarifa.ServicioId,
                    NombreServicio = tarifa.NombreServicio,
                    Coste = coste
                });
            }

            return opciones.OrderBy(o => o.Coste).ToList();
        }

        // El destino es España si no se informa país o es "ES". El resto de países (UE, etc.) los
        // resuelve cada tarifa por su cuenta (p.ej. GLS internacional); Canarias solo aplica en España.
        private static bool EsEspana(string paisIso)
        {
            string iso = (paisIso ?? string.Empty).Trim().ToUpperInvariant();
            return iso.Length == 0 || iso == "ES";
        }
    }
}
