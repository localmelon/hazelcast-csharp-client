using System;
using Hazelcast.IO;
using Hazelcast.Core;

namespace Hazelcast.Cluster
{
	public class Bind : DataSerializable{
    
	    Address address;
		
	
	    public Bind() {
	    }
	
	    public Bind(Address localAddress) {
	        address = localAddress;
	    }
	
	    public override String ToString() {
	        return "Bind " + address;
	    }
	
	    public void readData(IDataInput din) {
	        address = new Address();
	        address.readData(din);
	    }
	    
	    public void writeData(IDataOutput dout){
	        address.writeData(dout);
	    }
	}
}
