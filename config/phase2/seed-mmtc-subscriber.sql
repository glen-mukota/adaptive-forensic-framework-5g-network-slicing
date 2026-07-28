-- Research-only virtual mMTC subscriber. This script is idempotent and contains no real subscriber data.
INSERT INTO SessionManagementSubscriptionData
  (ueid, servingPlmnid, singleNssai, dnnConfigurations)
VALUES
  (
    '001010000000002',
    '00101',
    JSON_OBJECT('sst', 3, 'sd', 'FFFFFF'),
    JSON_OBJECT(
      'mmtc',
      JSON_OBJECT(
        'pduSessionTypes', JSON_OBJECT('defaultSessionType', 'IPV4'),
        'sscModes', JSON_OBJECT('defaultSscMode', 'SSC_MODE_1'),
        '5gQosProfile', JSON_OBJECT(
          '5qi', 9,
          'arp', JSON_OBJECT('priorityLevel', 15, 'preemptCap', 'NOT_PREEMPT', 'preemptVuln', 'PREEMPTABLE'),
          'priorityLevel', 1
        ),
        'sessionAmbr', JSON_OBJECT('uplink', '5Mbps', 'downlink', '5Mbps'),
        'staticIpAddress', JSON_ARRAY(JSON_OBJECT('ipv4Addr', '10.0.2.2'))
      )
    )
  )
ON DUPLICATE KEY UPDATE
  singleNssai = VALUES(singleNssai),
  dnnConfigurations = VALUES(dnnConfigurations);
