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
                    Fecha = e.Fecha
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
    }
}