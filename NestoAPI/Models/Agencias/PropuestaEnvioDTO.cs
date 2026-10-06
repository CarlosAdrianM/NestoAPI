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
        public decimal PrepagosPendientes { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        /// <summary>El id de país de la agencia (34 España, 351 Portugal; 724 en Correos Express), como EnviosAgencia.Pais.</summary>
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
    }
}
