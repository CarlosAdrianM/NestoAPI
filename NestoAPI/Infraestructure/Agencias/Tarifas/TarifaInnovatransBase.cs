using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Núcleo común a los servicios de Innovatrans (oferta Paquetería 2026, origen ES-28119
    /// Algete). Portado de Nesto. Cada servicio (Economy, 14H Portugal, Marítimo islas) aporta su
    /// tabla de precios y su coste de kilo adicional.
    ///
    /// IMPORTANTE: a diferencia del Nesto original, aquí los precios son ANTES de fuel y NO se
    /// aplica el 2,5% en la tarifa. El recargo de combustible lo aplica el comparador a partir de
    /// AgenciasTransporte.RecargoCombustible (la fila Numero=12 debe llevar 0,025 = 2,5%).
    /// </summary>
    public abstract class TarifaInnovatransBase : ITarifaAgencia
    {
        // Sending=10, Canteras=11 → Innovatrans=12. Debe coincidir con AgenciasTransporte.Numero.
        public int AgenciaId => 12;

        public byte HorarioDefectoId => 1; // Normal

        public abstract byte ServicioId { get; }
        public abstract string NombreServicio { get; }
        public abstract IReadOnlyList<TramoCosteEnvio> CosteEnvio { get; }
        public abstract decimal CosteKiloAdicional(ZonasEnvioAgencia zona);

        /// <summary>
        /// Reembolso (condición particular 5 de la oferta): 5% del valor, mínimo 4,03€, máximo
        /// 300€. No se le aplica fuel.
        /// </summary>
        public decimal CosteReembolso(decimal reembolso)
        {
            const decimal porcentaje = 0.05m;
            const decimal minimo = 4.03m;
            const decimal maximo = 300m;
            decimal comision = reembolso * porcentaje;
            if (comision < minimo)
            {
                return minimo;
            }
            if (comision > maximo)
            {
                return maximo;
            }
            return comision;
        }
    }
}
