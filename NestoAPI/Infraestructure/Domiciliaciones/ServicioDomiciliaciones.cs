using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.Domiciliaciones;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.Domiciliaciones
{
    public class ServicioDomiciliaciones : IServicioDomiciliaciones
    {
        public ICollection<EfectoDomiciliado> LeerDomiciliacionesDia(DateTime dia)
        {
            using (NVEntities db = new NVEntities())
            {
                var efectos = db.ExtractosCliente.Where(e => e.Fecha == dia && e.TipoApunte == Constantes.TiposExtractoCliente.PAGO && e.Remesa != null && e.CCC != null);

                List<EfectoDomiciliado> listaEfectos = efectos.Select(e => new EfectoDomiciliado
                {
                    Empresa = e.Empresa.Trim(),
                    Cliente = e.Número.Trim(),
                    Contacto = e.Contacto.Trim(),
                    Ccc = e.CCC.Trim(),
                    Concepto = e.Concepto.Trim(),
                    Importe = -e.Importe,
                    Fecha = e.Fecha,
                    NOrden = e.Nº_Orden,
                    NumeroDocumento = e.Nº_Documento != null ? e.Nº_Documento.Trim() : null,
                    Efecto = e.Efecto != null ? e.Efecto.Trim() : null
                }).ToList();

                foreach(var efecto in listaEfectos)
                {
                    var ccc = db.CCCs.Single(c => c.Empresa == efecto.Empresa && c.Cliente == efecto.Cliente && c.Contacto == efecto.Contacto && c.Número == efecto.Ccc);
                    string iban = $"{ccc.Pais}{ccc.DC_IBAN}{ccc.Entidad}{ccc.Oficina}{ccc.DC}{ccc.Nº_Cuenta}";
                    efecto.Iban = new Iban(iban);

                    var personas = db.PersonasContactoClientes.Where(p => p.Empresa == efecto.Empresa && p.NºCliente == efecto.Cliente && p.Contacto == efecto.Contacto && p.CorreoElectrónico != null);
                    var personasCobros = personas.Where(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_COBROS && p.CorreoElectrónico.Trim() != string.Empty);
                    if (!personasCobros.Any())
                    {
                        personasCobros = personas.Where(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO && p.CorreoElectrónico.Trim() != string.Empty);
                    }
                    if (!personasCobros.Any())
                    {
                        personasCobros = personas.Take(1);
                    }
                    efecto.Correo = String.Join(", ", personasCobros.Select(p => p.CorreoElectrónico.Trim()));
                    efecto.NombrePersona = String.Join(", ", personasCobros.Where(p => p.Saludo != null && p.Saludo.Trim() != string.Empty).Select(p => p.Saludo.Trim()));
                }
                return listaEfectos.ToList();
            }
        }

        public List<DocumentoRelacionado> BuscarDocumentosRelacionados(string empresa, int nOrden)
        {
            using (NVEntities db = new NVEntities())
            {
                var liquidaciones = db.LiquidacionesClientes
                    .Where(l => l.Empresa == empresa && (l.Nº_Orden == nOrden || l.Nº_Orden_Liq == nOrden))
                    .ToList();

                var documentos = new List<DocumentoRelacionado>();

                foreach (var liquidacion in liquidaciones)
                {
                    int nOrdenRelacionado = liquidacion.Nº_Orden == nOrden
                        ? liquidacion.Nº_Orden_Liq
                        : liquidacion.Nº_Orden;

                    decimal importe = liquidacion.Nº_Orden == nOrden
                        ? liquidacion.Importe
                        : -liquidacion.Importe;

                    var extracto = db.ExtractosCliente
                        .FirstOrDefault(e => e.Empresa == empresa && e.Nº_Orden == nOrdenRelacionado);

                    if (extracto != null && !string.IsNullOrWhiteSpace(extracto.Nº_Documento))
                    {
                        documentos.Add(new DocumentoRelacionado
                        {
                            NumeroDocumento = extracto.Nº_Documento.Trim(),
                            Importe = importe,
                            Descripcion = importe >= 0
                                ? $"Factura {extracto.Nº_Documento.Trim()} ({importe:N2} €)"
                                : $"Abono {extracto.Nº_Documento.Trim()} ({importe:N2} €)"
                        });
                    }
                }

                return documentos;
            }
        }
    }
}