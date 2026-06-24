using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Tarifa GLS/ASM "EuroBusinessParcel" (oferta GLS_EU.pdf 2026, cliente nueva vision, servicio 74).
    /// Es la tarifa INTERNACIONAL de GLS para la UE/Europa. A diferencia de las nacionales, NO se zonifica
    /// por código postal: la zona (A–E) la determina el PAÍS de destino. Por eso NO hereda de
    /// <see cref="TarifaNacionalBase"/> (esa base resuelve la zona por CP español/portugués); esta tarifa
    /// resuelve su zonificación A–E PUERTAS ADENTRO, sin imponer las zonas de GLS al canónico compartido.
    ///
    /// Precios de la oferta, ANTES de fuel y de Climate Protect: al porte se le aplica el recargo de
    /// combustible (AgenciasTransporte.RecargoCombustible) y un 1,50% Climate Protect. Ambos son
    /// porcentajes sobre el porte, así que el orden es indiferente (conmutativo).
    ///
    /// Sin kilo adicional (la oferta lo marca "n/d"): el último tramo (30 kg) cubre hasta el peso máximo
    /// real de 40 kg; por encima de 40 kg (o conversión volumétrica) hay que consultar -> no se modela.
    ///
    /// NO modelado (suplementos especiales de la oferta, raros): Córcega (+15 € sobre zona A; no se puede
    /// detectar desde el país FR, necesita la región del CP). Sí se aplican los suplementos por país
    /// Noruega (+8 €), Serbia (+10 €) y Grecia (banda por peso).
    /// </summary>
    public class TarifaGLSEuroBusinessParcel : ITarifaAgencia
    {
        private const decimal ClimateProtect = 0.015m;   // 1,50% Climate Protect (sobre el porte)
        private const decimal CashServiceReembolso = 6.00m; // CashService (contra reembolso); oferta: solo Italia
        private const decimal PesoMaximoReal = 40m;      // por encima -> consultar (no se modela)

        public int AgenciaId => 1;            // GLS/ASM
        public byte ServicioId => 74;         // EuroBusinessParcel (código ASM/GLS; 54=EuroEstándar, 76=Small)
        public string NombreServicio => "EuroBusinessParcel";
        public byte HorarioDefectoId => 18;   // mismo horario por defecto que BusinessParcel

        private enum ZonaEuropaGls { A, B, C, D, E }

        // País (ISO 3166-1 alpha-2) -> zona A–E de la oferta GLS_EU.pdf 2026. ES/PT NO están: España es
        // nacional y Portugal va por la tarifa nacional GLS / Innovatrans (esta tarifa los deja sin cubrir).
        private static readonly IReadOnlyDictionary<string, ZonaEuropaGls> _zonaPorPais =
            new Dictionary<string, ZonaEuropaGls>
            {
                // Zona A
                { "DE", ZonaEuropaGls.A }, { "FR", ZonaEuropaGls.A }, { "MC", ZonaEuropaGls.A },
                // Zona B
                { "AT", ZonaEuropaGls.B }, { "BE", ZonaEuropaGls.B }, { "IT", ZonaEuropaGls.B },
                { "LU", ZonaEuropaGls.B }, { "NL", ZonaEuropaGls.B }, { "GB", ZonaEuropaGls.B },
                { "SM", ZonaEuropaGls.B }, { "VA", ZonaEuropaGls.B },
                // Zona C
                { "DK", ZonaEuropaGls.C }, { "SK", ZonaEuropaGls.C }, { "SI", ZonaEuropaGls.C },
                { "LI", ZonaEuropaGls.C }, { "PL", ZonaEuropaGls.C }, { "CZ", ZonaEuropaGls.C },
                { "CH", ZonaEuropaGls.C },
                // Zona D
                { "BG", ZonaEuropaGls.D }, { "HR", ZonaEuropaGls.D }, { "EE", ZonaEuropaGls.D },
                { "FI", ZonaEuropaGls.D }, { "GR", ZonaEuropaGls.D }, { "HU", ZonaEuropaGls.D },
                { "IE", ZonaEuropaGls.D }, { "LT", ZonaEuropaGls.D }, { "LV", ZonaEuropaGls.D },
                { "NO", ZonaEuropaGls.D }, { "RO", ZonaEuropaGls.D }, { "RS", ZonaEuropaGls.D },
                { "SE", ZonaEuropaGls.D },
                // Zona E
                { "CY", ZonaEuropaGls.E }, { "MT", ZonaEuropaGls.E }
            };

        // Tramos "hasta X kg" -> precio por zona (€, antes de fuel/climate). Orden ascendente por peso.
        // El último tramo de 30 kg se extiende a 40 kg (peso máximo real), porque no hay kilo adicional.
        private static readonly IReadOnlyDictionary<ZonaEuropaGls, IReadOnlyList<(decimal PesoMaximo, decimal Precio)>> _tramos =
            new Dictionary<ZonaEuropaGls, IReadOnlyList<(decimal, decimal)>>
            {
                [ZonaEuropaGls.A] = new (decimal, decimal)[]
                {
                    (1m, 14.88m), (2m, 18.81m), (3m, 21.20m), (5m, 24.01m), (7m, 24.71m),
                    (10m, 25.55m), (15m, 27.94m), (20m, 30.89m), (PesoMaximoReal, 34.26m)
                },
                [ZonaEuropaGls.B] = new (decimal, decimal)[]
                {
                    (1m, 15.16m), (2m, 23.59m), (3m, 24.01m), (5m, 27.38m), (7m, 28.64m),
                    (10m, 30.05m), (15m, 31.31m), (20m, 33.98m), (PesoMaximoReal, 36.93m)
                },
                [ZonaEuropaGls.C] = new (decimal, decimal)[]
                {
                    (1m, 21.06m), (2m, 32.43m), (3m, 32.43m), (5m, 35.24m), (7m, 35.52m),
                    (10m, 37.35m), (15m, 38.47m), (20m, 41.56m), (PesoMaximoReal, 44.65m)
                },
                [ZonaEuropaGls.D] = new (decimal, decimal)[]
                {
                    (1m, 29.77m), (2m, 56.02m), (3m, 56.02m), (5m, 57.57m), (7m, 58.41m),
                    (10m, 61.36m), (15m, 62.48m), (20m, 63.60m), (PesoMaximoReal, 77.22m)
                },
                [ZonaEuropaGls.E] = new (decimal, decimal)[]
                {
                    (1m, 54.90m), (2m, 67.40m), (3m, 72.03m), (5m, 100.25m), (7m, 131.28m),
                    (10m, 172.14m), (15m, 227.46m), (20m, 294.01m), (PesoMaximoReal, 421.50m)
                }
            };

        public decimal CalcularCoste(string codigoPostal, string paisIso, decimal peso, decimal reembolso, decimal recargoCombustible)
        {
            string iso = (paisIso ?? string.Empty).Trim().ToUpperInvariant();
            if (!_zonaPorPais.TryGetValue(iso, out ZonaEuropaGls zona))
            {
                return decimal.MaxValue; // país fuera de la tarifa EuroBusinessParcel: no la cubre
            }

            // Contra reembolso (CashService) solo está disponible para Italia en esta oferta.
            if (reembolso != 0 && iso != "IT")
            {
                return decimal.MaxValue;
            }

            (decimal PesoMaximo, decimal Precio) tramo = _tramos[zona].FirstOrDefault(t => peso <= t.PesoMaximo);
            if (tramo.Precio == 0m) // ningún tramo cubre el peso (> 40 kg / volumétrico): consultar
            {
                return decimal.MaxValue;
            }

            decimal porte = tramo.Precio + SuplementoPais(iso, peso);
            // Fuel y Climate Protect, ambos porcentajes sobre el porte (conmutativos entre sí).
            porte = porte * (1m + recargoCombustible) * (1m + ClimateProtect);

            decimal costeReembolso = reembolso != 0 ? CashServiceReembolso : 0m;
            return porte + costeReembolso;
        }

        // Suplementos por país que se SUMAN al porte de la zona (oferta, sección Observaciones):
        //   Noruega +8 €, Serbia +10 € (planos por tramo); Grecia por banda de peso.
        private static decimal SuplementoPais(string iso, decimal peso)
        {
            switch (iso)
            {
                case "NO": return 8m;
                case "RS": return 10m;
                case "GR":
                    if (peso <= 3m) return 1m;
                    if (peso <= 10m) return 16m;
                    if (peso <= 25m) return 40m;
                    return 50m; // hasta 40 kg
                default: return 0m;
            }
        }
    }
}
