using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Un tramo de la tabla de precios de un servicio: hasta <see cref="PesoMaximo"/> kilos,
    /// en la zona <see cref="Zona"/>, cuesta <see cref="Precio"/> (antes de fuel).
    /// </summary>
    public class TramoCosteEnvio
    {
        public TramoCosteEnvio(decimal pesoMaximo, ZonasEnvioAgencia zona, decimal precio)
        {
            PesoMaximo = pesoMaximo;
            Zona = zona;
            Precio = precio;
        }

        public decimal PesoMaximo { get; }
        public ZonasEnvioAgencia Zona { get; }
        public decimal Precio { get; }
    }

    /// <summary>
    /// Tarifa de un servicio de una agencia. El contrato común es AGNÓSTICO de agencia: recibe el
    /// destino CANÓNICO (código postal + país en ISO 3166-1 alpha-2) y devuelve el coste total (porte
    /// con fuel/recargos + reembolso) o <see cref="decimal.MaxValue"/> si la tarifa NO cubre ese destino.
    /// CADA agencia decide su propia zonificación PUERTAS ADENTRO (las nacionales reúsan el helper
    /// común de zonas por CP; GLS internacional usa sus zonas A–E; otra agencia, las suyas), sin
    /// imponer ningún sistema de zonas en lo compartido.
    /// </summary>
    public interface ITarifaAgencia
    {
        int AgenciaId { get; }
        byte ServicioId { get; }
        string NombreServicio { get; }
        byte HorarioDefectoId { get; }

        /// <summary>
        /// Coste total para el destino (CP + país ISO2), con fuel y recargos ya aplicados, o
        /// <see cref="decimal.MaxValue"/> si esta tarifa no cubre el destino.
        /// </summary>
        decimal CalcularCoste(string codigoPostal, string paisIso, decimal peso, decimal reembolso, decimal recargoCombustible);
    }

    /// <summary>
    /// NestoAPI#505: marca un servicio que el comparador NUNCA propone (más caro, p. ej. CTT 24h). Solo
    /// entra cuando se pide expresamente por su ServicioId (<see cref="ComparadorAgencias.CosteDeAgencia"/>
    /// con servicioId), porque el usuario lo ha forzado a mano.
    /// </summary>
    public interface ITarifaSoloAPeticion
    {
    }

    /// <summary>
    /// NestoAPI#494: la tarifa tiene PRECIO DE RETORNO (recogida en el domicilio de un cliente o de un
    /// proveedor que viene a nuestro almacén). Regla de Carlos (17/09/26): solo las agencias con el
    /// precio del retorno informado entran en la subasta de retornos; la que no lo implementa queda
    /// fuera hasta que nos pasen ese precio (mismo mecanismo que una tarifa que no cubre: MaxValue).
    /// </summary>
    public interface ITarifaConRetorno
    {
        /// <summary>
        /// Coste del retorno desde el domicilio donde se recoge (CP + país ISO2) hasta nuestro almacén,
        /// con fuel, o <see cref="decimal.MaxValue"/> si no lo cubre. Un retorno no lleva reembolso.
        /// </summary>
        decimal CalcularCosteRetorno(string codigoPostal, string paisIso, decimal peso, decimal recargoCombustible);
    }

    /// <summary>
    /// NestoAPI#494: qué se subasta en el comparador. <see cref="Envio"/> es lo de siempre;
    /// <see cref="Retorno"/>, solo la recogida en el cliente/proveedor; <see cref="EnvioYRetorno"/>,
    /// el envío y su retorno juntos (suma de los dos costes, por la MISMA agencia).
    /// </summary>
    public enum ModoComparacionAgencia
    {
        Envio = 0,
        Retorno = 1,
        EnvioYRetorno = 2
    }

    /// <summary>Capacidades de una tarifa, mirando a través de los decoradores (freno por zonas).</summary>
    public static class CapacidadesTarifa
    {
        public static bool EsSoloAPeticion(ITarifaAgencia tarifa)
        {
            if (tarifa is ITarifaSoloAPeticion) return true;
            return tarifa is TarifaConZonasActivas decorada && EsSoloAPeticion(decorada.Interior);
        }

        /// <summary>NestoAPI#494: coste del retorno, o MaxValue si la tarifa no tiene precio de retorno.</summary>
        public static decimal CosteRetorno(ITarifaAgencia tarifa, string codigoPostal, string paisIso, decimal peso, decimal recargoCombustible)
            => tarifa is ITarifaConRetorno conRetorno
                ? conRetorno.CalcularCosteRetorno(codigoPostal, paisIso, peso, recargoCombustible)
                : decimal.MaxValue;

        /// <summary>
        /// NestoAPI#494: coste de la tarifa en el modo pedido. Envío = lo de siempre; Retorno = solo la
        /// recogida; EnvioYRetorno = la suma (MaxValue si falta cualquiera de los dos).
        /// </summary>
        public static decimal Coste(ITarifaAgencia tarifa, ModoComparacionAgencia modo, string codigoPostal, string paisIso,
            decimal peso, decimal reembolso, decimal recargoCombustible)
        {
            switch (modo)
            {
                case ModoComparacionAgencia.Retorno:
                    return CosteRetorno(tarifa, codigoPostal, paisIso, peso, recargoCombustible);
                case ModoComparacionAgencia.EnvioYRetorno:
                    decimal retorno = CosteRetorno(tarifa, codigoPostal, paisIso, peso, recargoCombustible);
                    if (retorno == decimal.MaxValue) return decimal.MaxValue;
                    decimal envio = tarifa.CalcularCoste(codigoPostal, paisIso, peso, reembolso, recargoCombustible);
                    return envio == decimal.MaxValue ? decimal.MaxValue : envio + retorno;
                default:
                    return tarifa.CalcularCoste(codigoPostal, paisIso, peso, reembolso, recargoCombustible);
            }
        }
    }
}
