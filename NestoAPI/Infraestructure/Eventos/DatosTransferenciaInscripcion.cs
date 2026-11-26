using NestoAPI.Infraestructure.Cobros;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.Cobros;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// NestoAPI#887 (duda de Paloma, novedad 1421; aprobado por Carlos 23/11/26): lo que necesita el cliente para pagar por transferencia la
    /// señal de una inscripción. GET api/Eventos/Inscripciones/{id}/DatosTransferencia. Como el de un pedido de prepago (sugerencia 396).
    /// </summary>
    public class DatosTransferenciaInscripcionDTO
    {
        public int InscripcionId { get; set; }
        public int EventoId { get; set; }
        public string Evento { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>IBAN formateado de la empresa (si hay varias cuentas, «ES.. o ES..»). Null si no hay.</summary>
        public string Iban { get; set; }
        /// <summary>Beneficiario (Empresas.Nombre).</summary>
        public string Titular { get; set; }
        /// <summary>«Cliente 29606 - INS123»: el número de cliente y el documento del cobro en Cajas.</summary>
        public string Concepto { get; set; }
        /// <summary>La señal de la inscripción (según su modalidad), IVA incluido.</summary>
        public decimal Importe { get; set; }
        /// <summary>Los datos listos para pegar en un correo o un WhatsApp, una línea por dato.</summary>
        public string Texto { get; set; }
        /// <summary>Para quien lo copia (no para el cliente): quién apunta el cobro cuando llega la transferencia.</summary>
        public string Recordatorio { get; set; }
    }

    /// <summary>NestoAPI#887: LAS REGLAS (puras).</summary>
    public static class ReglasDatosTransferenciaInscripcion
    {
        public const string MENSAJE_CLIENTE_TIENDA = "Los datos de la transferencia de una inscripción los da la casa.";

        /// <summary>«Cliente 29606 - INS123».</summary>
        public static string Concepto(string cliente, int inscripcionId) =>
            $"Cliente {cliente?.Trim()} - {ServicioInscripcionesEventos.DocumentoPago(inscripcionId)}";

        /// <summary>Solo una reserva (Reservada) de pago tiene señal por pagar. Null si vale; si no, el motivo (de usted, 409).</summary>
        public static string MotivoNoAplica(EventoInscripcion inscripcion, Evento evento)
        {
            EstadoInscripcionEvento? estado = ReglasInscripcionEvento.Estado(inscripcion?.Estado);
            if (estado != EstadoInscripcionEvento.Reservada)
            {
                string texto = estado.HasValue ? ReglasInscripcionEvento.Texto(estado.Value).ToLowerInvariant() : "sin estado";
                return $"La inscripción {inscripcion?.Id} está {texto}: solo una reserva sin pagar tiene señal que pagar por transferencia.";
            }
            if (ReglasModalidadEvento.ImporteDe(inscripcion, evento) <= 0)
            {
                return $"La inscripción {inscripcion.Id} es gratuita: no hay nada que pagar.";
            }
            return null;
        }

        /// <summary>Los datos de la transferencia de esa inscripción (sin mirar si aplica).</summary>
        public static DatosTransferenciaInscripcionDTO Construir(EventoInscripcion inscripcion, Evento evento, DatosPagoAviso datosPago)
        {
            string cliente = inscripcion?.Cliente?.Trim();
            var datos = new DatosTransferenciaInscripcionDTO
            {
                InscripcionId = inscripcion?.Id ?? 0,
                EventoId = evento?.Id ?? 0,
                Evento = evento?.Titulo?.Trim(),
                Empresa = string.IsNullOrWhiteSpace(inscripcion?.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : inscripcion.Empresa.Trim(),
                Cliente = cliente,
                Contacto = inscripcion?.Contacto?.Trim(),
                Iban = string.IsNullOrWhiteSpace(datosPago?.Iban) ? null : datosPago.Iban.Trim(),
                Titular = string.IsNullOrWhiteSpace(datosPago?.Titular) ? null : datosPago.Titular.Trim(),
                Concepto = Concepto(cliente, inscripcion?.Id ?? 0),
                Importe = Math.Round(ReglasModalidadEvento.ImporteDe(inscripcion, evento), 2, MidpointRounding.AwayFromZero)
            };
            datos.Texto = Texto(datos);
            datos.Recordatorio = $"Cuando llegue la transferencia, Administración apunta el cobro en Cajas › Cobros (documento " +
                $"{ServicioInscripcionesEventos.DocumentoPago(datos.InscripcionId)}).";
            return datos;
        }

        /// <summary>Una línea por dato, para pegar en un correo o un WhatsApp. Lo que falte no sale.</summary>
        public static string Texto(DatosTransferenciaInscripcionDTO datos)
        {
            var lineas = new List<string>();
            if (!string.IsNullOrWhiteSpace(datos.Iban))
            {
                lineas.Add($"IBAN: {datos.Iban}");
            }
            if (!string.IsNullOrWhiteSpace(datos.Titular))
            {
                lineas.Add($"Beneficiario: {datos.Titular}");
            }
            lineas.Add($"Concepto: {datos.Concepto}");
            if (datos.Importe > 0)
            {
                lineas.Add($"Importe: {PlantillaAvisoFacturaVencida.FormatearImporte(datos.Importe)}");
            }
            return string.Join(Environment.NewLine, lineas);
        }

        /// <summary>
        /// La frase de los avisos al cliente (plazo de pago, evento lleno) cuando paga por transferencia y se tienen los datos: en vez de
        /// «pídanoslos», el beneficiario, el IBAN, el importe y el concepto. Null si faltan (el aviso dice lo de siempre).
        /// </summary>
        public static string FraseAviso(DatosTransferenciaInscripcionDTO datos)
        {
            if (datos == null || string.IsNullOrWhiteSpace(datos.Iban))
            {
                return null;
            }
            string aQuien = string.IsNullOrWhiteSpace(datos.Titular) ? string.Empty : $" a {datos.Titular}";
            string importe = datos.Importe > 0 ? $" de {PlantillaAvisoFacturaVencida.FormatearImporte(datos.Importe)}" : string.Empty;
            return $"Puede pagarla con una transferencia{importe}{aQuien} (IBAN {datos.Iban}), indicando en el concepto «{datos.Concepto}», o en su tienda.";
        }
    }

    public interface IServicioDatosTransferenciaInscripcion
    {
        Task<DatosTransferenciaInscripcionDTO> Leer(int inscripcionId, IPrincipal usuario);
    }

    /// <summary>NestoAPI#887: solo lectura. 404 sin inscripción, 409 si no es una reserva de pago, 400 si la empresa no tiene cuenta.</summary>
    public class ServicioDatosTransferenciaInscripcion : IServicioDatosTransferenciaInscripcion
    {
        private readonly NVEntities db;
        private readonly ILectorDatosPagoEmpresa lectorDatosPago;

        public ServicioDatosTransferenciaInscripcion(NVEntities db, ILectorDatosPagoEmpresa lectorDatosPago = null)
        {
            this.db = db;
            this.lectorDatosPago = lectorDatosPago ?? new LectorDatosPagoEmpresa(db);
        }

        public async Task<DatosTransferenciaInscripcionDTO> Leer(int inscripcionId, IPrincipal usuario)
        {
            if (PermisosEventos.ClienteDelToken(usuario) != null)
            {
                throw new NestoBusinessException(ReglasDatosTransferenciaInscripcion.MENSAJE_CLIENTE_TIENDA) { StatusCode = HttpStatusCode.Forbidden };
            }
            EventoInscripcion inscripcion = await db.EventosInscripciones.Where(i => i.Id == inscripcionId).FirstOrDefaultAsync().ConfigureAwait(false)
                ?? throw new NestoBusinessException($"No existe la inscripción {inscripcionId}.") { StatusCode = HttpStatusCode.NotFound };
            Evento evento = await db.Eventos.Where(e => e.Id == inscripcion.EventoId).FirstOrDefaultAsync().ConfigureAwait(false)
                ?? throw new NestoBusinessException($"No existe el evento de la inscripción {inscripcionId}.") { StatusCode = HttpStatusCode.NotFound };
            string motivo = ReglasDatosTransferenciaInscripcion.MotivoNoAplica(inscripcion, evento);
            if (motivo != null)
            {
                throw new NestoBusinessException(motivo) { StatusCode = HttpStatusCode.Conflict };
            }
            string empresa = string.IsNullOrWhiteSpace(inscripcion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : inscripcion.Empresa.Trim();
            DatosTransferenciaInscripcionDTO datos = ReglasDatosTransferenciaInscripcion.Construir(inscripcion, evento, lectorDatosPago.Leer(empresa));
            if (string.IsNullOrWhiteSpace(datos.Iban))
            {
                throw new NestoBusinessException($"La empresa {empresa} no tiene ninguna cuenta bancaria para recibir transferencias.");
            }
            return datos;
        }
    }
}
