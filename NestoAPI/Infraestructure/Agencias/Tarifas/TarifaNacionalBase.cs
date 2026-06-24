using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Base para tarifas que se zonifican por CÓDIGO POSTAL nacional (España + Portugal): GLS, CTT,
    /// Innovatrans… Cada tarifa concreta aporta sus tramos, su coste por kilo adicional y su coste de
    /// reembolso; la base resuelve la zona nacional desde (CP, país) y delega el cálculo en
    /// <see cref="CalculadoraCosteEnvio"/>. La zonificación nacional es un detalle COMPARTIDO por
    /// conveniencia (DRY), no un canónico impuesto: una agencia con zonas distintas (p.ej. la
    /// internacional de GLS) NO hereda de aquí y resuelve su coste por su cuenta.
    /// </summary>
    public abstract class TarifaNacionalBase : ITarifaAgencia
    {
        public abstract int AgenciaId { get; }
        public abstract byte ServicioId { get; }
        public abstract string NombreServicio { get; }
        public abstract byte HorarioDefectoId { get; }

        /// <summary>Tabla de precios por tramo de peso y zona nacional (antes de fuel).</summary>
        protected abstract IReadOnlyList<TramoCosteEnvio> Tramos { get; }

        /// <summary>Coste por kilo adicional por encima del último tramo de la zona.</summary>
        protected abstract decimal CosteKiloAdicional(ZonasEnvioAgencia zona);

        /// <summary>Comisión de reembolso (no se le aplica fuel).</summary>
        protected abstract decimal CosteReembolso(decimal reembolso);

        public decimal CalcularCoste(string codigoPostal, string paisIso, decimal peso, decimal reembolso, decimal recargoCombustible)
        {
            ZonasEnvioAgencia zona = ZonaNacional(codigoPostal, paisIso);
            decimal costeReembolso = reembolso != 0 ? CosteReembolso(reembolso) : 0m;
            return CalculadoraCosteEnvio.CalcularCoste(Tramos, CosteKiloAdicional, zona, peso, recargoCombustible, costeReembolso);
        }

        // Para España (o país vacío/no informado) la zona sale del CP (incluida la detección de
        // Portugal por formato de CP). Para "PT" explícito, Portugal. Para cualquier otro país, las
        // tarifas nacionales no cubren -> Extranjero (sin tramos -> coste MaxValue).
        private static ZonasEnvioAgencia ZonaNacional(string codigoPostal, string paisIso)
        {
            string iso = (paisIso ?? string.Empty).Trim().ToUpperInvariant();
            if (iso == "PT")
            {
                return ZonasEnvioAgencia.Portugal;
            }
            if (iso.Length == 0 || iso == "ES")
            {
                return CalculadoraZonaEnvio.CalcularZona(codigoPostal);
            }
            return ZonasEnvioAgencia.Extranjero;
        }
    }
}
