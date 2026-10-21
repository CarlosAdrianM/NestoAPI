using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Agencias
{
    /// <summary>
    /// NestoAPI#595 (slice 1): el envío de agencia de un pedido tal como lo grabaría hoy Agencias de Nesto
    /// (AgenciasViewModel.ActualizarPedidoSeleccionado + InsertarRegistro), calculado en el servidor. De momento
    /// solo se lee (GET api/EnviosAgencias/Propuesta) y se compara en sombra con lo que graba Nesto; el slice 2
    /// la usará para crear el envío desde el pedido.
    /// </summary>
    public class PropuestaEnvioDTO
    {
        public const string ORIGEN_NUEVO = "Nuevo";
        public const string ORIGEN_PENDIENTE_REUTILIZADO = "PendienteReutilizado";
        public const string ORIGEN_AMPLIACION = "Ampliacion";

        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>Número de la agencia (AgenciasTransporte.Numero). 0 si ninguna agencia cubre el destino.</summary>
        public int Agencia { get; set; }
        public string AgenciaNombre { get; set; }
        public short Servicio { get; set; }
        public short Horario { get; set; }
        public short Retorno { get; set; }
        public short Bultos { get; set; }
        public decimal Peso { get; set; }
        /// <summary>Lo que cobrará la agencia. NO descuenta los prepagos (#569): eso va en <see cref="Avisos"/>.</summary>
        public decimal Reembolso { get; set; }
        /// <summary>
        /// Lo ya pagado por adelantado: prepagos sin facturar (#569) y, desde NestoAPI#679, lo cobrado con enlaces de pago
        /// del pedido (<see cref="EnlacesPagoAutorizados"/> va dentro). Es lo que se descuenta con DescontarPrepagos.
        /// </summary>
        public decimal PrepagosPendientes { get; set; }
        /// <summary>NestoAPI#679: la parte de <see cref="PrepagosPendientes"/> cobrada con enlaces de pago del pedido.</summary>
        public decimal EnlacesPagoAutorizados { get; set; }
        /// <summary>NestoAPI#679: los enlaces de pago del pedido aún sin pagar (los vivos, del más reciente al más antiguo).</summary>
        public List<Pagos.EnlacePagoPedido> EnlacesPagoPendientes { get; set; } = new List<Pagos.EnlacePagoPedido>();
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        /// <summary>El id de país de la agencia (34 España, 351 Portugal), como EnviosAgencia.Pais.</summary>
        public int Pais { get; set; }
        /// <summary>País del destino en ISO 3166-1 alfa-2, el que se pasa al comparador y al coste.</summary>
        public string PaisIso { get; set; }
        public string Telefono { get; set; }
        public string Movil { get; set; }
        public string Email { get; set; }
        public string Atencion { get; set; }
        /// <summary>Como mucho 80 caracteres (EnviosAgencia.Observaciones es varchar(80)).</summary>
        public string Observaciones { get; set; }
        public string Vendedor { get; set; }
        public DateTime Fecha { get; set; }
        public DateTime? FechaEntrega { get; set; }
        /// <summary>Coste de la agencia elegida (CosteDeAgencia); 0 si no se puede calcular.</summary>
        public decimal ImporteGasto { get; set; }
        /// <summary>«Nuevo», «PendienteReutilizado» o «Ampliacion».</summary>
        public string Origen { get; set; }
        /// <summary>Con Origen PendienteReutilizado, el envío pendiente que se reutilizaría; con Ampliacion, el que se ampliaría.</summary>
        public int? EnvioOrigen { get; set; }
        /// <summary>Lo que Nesto pregunta o avisa antes de insertar (sin picking, dirección propia, prepagos, dimensiones...).</summary>
        public List<string> Avisos { get; set; } = new List<string>();
        /// <summary>
        /// NestoAPI#595 (slice 2, 20/10/26): los mismos avisos que <see cref="Avisos"/> (mismos textos, mismo orden) con un
        /// código y un tipo para que el cliente sepa qué hacer con cada uno sin leer el texto. Avisos se mantiene.
        /// </summary>
        public List<AvisoPropuestaEnvio> AvisosDetalle { get; set; } = new List<AvisoPropuestaEnvio>();
    }

    /// <summary>NestoAPI#595 (slice 2, 20/10/26): un aviso de la propuesta de envío.</summary>
    public class AvisoPropuestaEnvio
    {
        /// <summary>Uno de <see cref="CodigosAvisoPropuestaEnvio"/>.</summary>
        public string Codigo { get; set; }
        /// <summary>El mismo texto que va en <see cref="PropuestaEnvioDTO.Avisos"/>.</summary>
        public string Texto { get; set; }
        /// <summary>Uno de <see cref="TiposAvisoPropuestaEnvio"/>.</summary>
        public string Tipo { get; set; }
    }

    /// <summary>NestoAPI#595 (slice 2, 20/10/26): los códigos de los avisos de la propuesta de envío.</summary>
    public static class CodigosAvisoPropuestaEnvio
    {
        /// <summary>El pedido no tiene ninguna línea con picking (Nesto pregunta si se inserta igualmente).</summary>
        public const string SIN_PICKING = "SinPicking";
        /// <summary>Es ampliación de un envío en curso: se actualizarían sus bultos.</summary>
        public const string AMPLIACION = "Ampliacion";
        /// <summary>El destino es la dirección de Nueva Visión.</summary>
        public const string DIRECCION_PROPIA = "DireccionPropia";
        /// <summary>Hay prepagos y el reembolso no los descuenta (Nesto ofrece descontarlos).</summary>
        public const string PREPAGOS = "Prepagos";
        /// <summary>La agencia (Canteras) necesita las dimensiones de los bultos.</summary>
        public const string DIMENSIONES = "Dimensiones";
        /// <summary>Ninguna agencia tiene tarifa para el destino.</summary>
        public const string SIN_AGENCIA = "SinAgencia";
        /// <summary>NestoAPI#679: el pedido tiene enlaces de pago sin pagar y sale contra reembolso.</summary>
        public const string ENLACE_PAGO_PENDIENTE = "EnlacePagoPendiente";
        /// <summary>NestoAPI#686: pedido al contado con «Mantener junto» y líneas pendientes: el envío sale sin reembolso.</summary>
        public const string CONTADO_MANTENER_JUNTO = "ContadoMantenerJunto";
    }

    /// <summary>NestoAPI#595 (slice 2, 20/10/26): qué tiene que hacer quien llama con cada aviso.</summary>
    public static class TiposAvisoPropuestaEnvio
    {
        /// <summary>Hay que confirmar para seguir (sí / no).</summary>
        public const string CONFIRMAR = "Confirmar";
        /// <summary>Hay que elegir entre opciones (por ejemplo, descontar o no los prepagos del reembolso).</summary>
        public const string ELEGIR = "Elegir";
        /// <summary>Falta un dato que hay que pedir (por ejemplo, las dimensiones de los bultos).</summary>
        public const string DATO = "Dato";
        /// <summary>No se puede crear el envío así.</summary>
        public const string BLOQUEANTE = "Bloqueante";
    }
}
