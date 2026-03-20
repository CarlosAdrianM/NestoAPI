using NestoAPI.Models.Domiciliaciones;
using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Domiciliaciones
{
    public interface IServicioDomiciliaciones
    {
        ICollection<EfectoDomiciliado> LeerDomiciliacionesDia(DateTime dia);
        List<DocumentoRelacionado> BuscarDocumentosRelacionados(string empresa, int nOrden);
    }
}