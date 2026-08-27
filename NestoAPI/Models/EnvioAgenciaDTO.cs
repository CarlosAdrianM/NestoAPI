using NestoAPI.Models.Agencias;
using System;

namespace NestoAPI.Models
{
    public class EnvioAgenciaDTO
    {
        public EnvioAgenciaDTO() { }
        public EnvioAgenciaDTO(EnviosAgencia envio)
        {
            if (envio == null)
            {
                throw new Exception("No se puede crear un envío nuevo desde un parámetro nulo");
            }
            Numero = envio.Numero;
            AgenciaId = envio.Agencia;
            AgenciaNombre = envio.AgenciasTransporte?.Nombre;
            AgenciaIdentificador = envio.AgenciasTransporte?.Identificador;
            Cliente = envio.Cliente;
            Pedido = (int)envio.Pedido;
            Estado = envio.Estado;
            Retorno = envio.Retorno;
            Fecha = envio.Fecha;
            CodigoBarras = envio.CodigoBarras;
            CodigoPostal = envio.CodPostal;
        }
        public int Numero { get; set; }
        public int AgenciaId { get; set; }
        public string AgenciaIdentificador { get; set; }
        public string AgenciaNombre { get; set; }
        public string Cliente { get; set; }
        public int Pedido { get; set; }
        public short Estado { get; set; }
        public short Retorno { get; set; }
        public DateTime Fecha { get; set; }
        public string CodigoBarras { get; set; }
        public string CodigoPostal { get; set; }
        public string EnlaceSeguimiento
        {
            get
            {
                // NestoAPI#240: la lógica por agencia vive en RegistroSeguimientoAgencias. Este DTO
                // conserva su contrato: "error, agencia no definida" si la agencia no se conoce y
                // cadena vacía si se conoce pero faltan datos para construir la URL.
                if (!RegistroSeguimientoAgencias.AgenciaConocida(AgenciaNombre))
                {
                    return "error, agencia no definida";
                }
                return RegistroSeguimientoAgencias.ConstruirUrl(DatosSeguimiento()) ?? string.Empty;
            }
        }

        private DatosSeguimientoEnvio DatosSeguimiento()
        {
            return new DatosSeguimientoEnvio
            {
                AgenciaNombre = AgenciaNombre,
                Identificador = AgenciaIdentificador,
                CodigoSeguimiento = CodigoBarras,
                CodigoPostal = CodigoPostal,
                Cliente = Cliente,
                Pedido = Pedido
            };
        }

        // NestoAPI#258 slice (a): identificadores por canal externo declarados por la agencia.
        // Los canales de Nesto (Prestashop/Amazon) confirman envíos con estos datos en vez de
        // re-parsear el enlace de seguimiento (LeerDatosEnvio gemelos). Null = agencia no
        // registrada o sin presencia en ese canal.

        /// <summary>Nº de seguimiento del envío (el CodigoBarras sin el padding de BD).</summary>
        public string NumeroSeguimiento => CodigoBarras?.Trim();

        /// <summary>Id del transportista en Prestashop (105 CEX, 103 Sending, 160 GLS/Innovatrans).</summary>
        public string TransportistaPrestashop => RegistroSeguimientoAgencias.Obtener(AgenciaNombre)?.TransportistaPrestashop;

        /// <summary>
        /// NestoAPI#417: lo que debe viajar como "nº de seguimiento" a Prestashop para que el
        /// cliente acabe viendo un ENLACE útil. Para las agencias del transportista genérico 160
        /// (plantilla vacía en la tienda) es el enlace completo sin esquema
        /// (mygls.gls-spain.es/e/{albarán}/{cp}); para CEX/Sending, el número pelado de siempre.
        /// </summary>
        public string TrackingPrestashop => RegistroSeguimientoAgencias.Obtener(AgenciaNombre)?.TrackingPrestashop(DatosSeguimiento());

        /// <summary>CarrierName para confirmar el envío en Amazon MFN.</summary>
        public string CarrierNameAmazon => RegistroSeguimientoAgencias.Obtener(AgenciaNombre)?.CarrierNameAmazon;

        /// <summary>ShippingMethod para confirmar el envío en Amazon MFN.</summary>
        public string ShippingMethodAmazon => RegistroSeguimientoAgencias.Obtener(AgenciaNombre)?.ShippingMethodAmazon;
    }
}